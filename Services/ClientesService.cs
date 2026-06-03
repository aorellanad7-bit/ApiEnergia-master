using System.Security.Cryptography;
using ApiEnergia.Auth;
using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using ApiEnergia.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiEnergia.Services
{
    public class ClientesService : IClientesService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;

        public ClientesService(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
        }

        // Máximo de reintentos al generar un número de contador para tolerar
        // colisiones con valores ya existentes (extremadamente improbables con
        // 8 hex chars = 16^8 combinaciones, pero defensivo).
        private const int MaxIntentosNumeroContador = 5;

        public async Task<CrearClienteConContadorResponse> CrearClienteConContadorAsync(CrearClienteConContadorRequest request)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            // Si ya existe un usuario con el DPI como nombre_usuario, abortamos
            // antes de tocar la BD. Sin esta verificación previa el INSERT en
            // usuario_acceso_energia tronaría con error 1062 por el UNIQUE
            // (uk_usuario_acceso_nombre_usuario), pero el cliente recibiría un
            // 500 sin diagnóstico útil.
            bool usuarioYaExiste = await _unitOfWork.Accesos
                .AnyAsync(u => u.NombreUsuario == request.Dpi);
            if (usuarioYaExiste)
                throw new InvalidOperationException(
                    $"Ya existe un usuario de portal con DPI '{request.Dpi}'.");

            // Password temporal aleatorio (no derivable del DPI). Lo generamos
            // FUERA de la transacción porque la lambda puede ejecutarse más de
            // una vez si la estrategia de reintentos lo decide; no queremos
            // que el password cambie entre intentos ni regenerarlo en vano.
            var passwordTemporal = GenerarPasswordTemporalSeguro();
            var passwordHash = _passwordHasher.Hash(passwordTemporal);

            // Toda la operación va dentro de una transacción explícita
            // envuelta en la IExecutionStrategy del provider: si cualquier
            // INSERT (cliente, contador, usuario) falla, hacemos rollback.
            // Sin la estrategia, MySqlRetryingExecutionStrategy + transacción
            // explícita son incompatibles y el INSERT lanza:
            //   "does not support user-initiated transactions".
            string numeroContador = string.Empty;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var cliente = await _unitOfWork.Clientes.FirstOrDefaultAsync(c => c.Dpi == request.Dpi);
                if (cliente is null)
                {
                    cliente = new ClienteLuz
                    {
                        Dpi = request.Dpi,
                        Nombre = request.Nombre,
                        Apellido = request.Apellido,
                        Correo = request.Correo
                    };

                    await _unitOfWork.Clientes.AddAsync(cliente);
                    // Necesitamos el IdCliente generado para asociarlo al usuario.
                    await _unitOfWork.SaveChangesAsync();
                }

                numeroContador = await GenerarNumeroContadorUnicoAsync();
                var contador = new ContadorEnergia
                {
                    NumeroContador = numeroContador,
                    DireccionInmueble = request.DireccionInmueble,
                    FechaInstalacion = DateTime.UtcNow,
                    Estado = "ACTIVO",
                    Cliente = cliente
                };

                await _unitOfWork.Accesos.AddAsync(new UsuarioAccesoEnergia
                {
                    IdCliente = cliente.IdCliente,
                    NombreUsuario = request.Dpi,
                    PasswordHash = passwordHash,
                    Rol = "CLIENTE",
                    DebeCambiarPassword = true
                });

                await _unitOfWork.Contadores.AddAsync(contador);

                await _unitOfWork.SaveChangesAsync();
            });

            return new CrearClienteConContadorResponse(numeroContador, request.Dpi, passwordTemporal);
        }

        public async Task<ResetPasswordClienteResponse> ResetearPasswordClienteAsync(string dpi)
        {
            dpi = dpi?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dpi))
                throw new ArgumentException("El DPI es obligatorio.", nameof(dpi));

            var usuario = await _unitOfWork.Accesos
                .FirstOrDefaultAsync(u => u.NombreUsuario == dpi && u.Rol == "CLIENTE");
            if (usuario is null)
                throw new InvalidOperationException($"No existe un usuario de portal con DPI '{dpi}'.");

            var passwordTemporal = GenerarPasswordTemporalSeguro();
            usuario.PasswordHash = _passwordHasher.Hash(passwordTemporal);
            usuario.DebeCambiarPassword = true;
            await _unitOfWork.SaveChangesAsync();

            return new ResetPasswordClienteResponse(dpi, passwordTemporal);
        }

        // Límites de paginación. Si el frontend pide algo fuera de rango,
        // normalizamos en lugar de tronar para que la API sea tolerante.
        private const int TamanoPaginaMinimo = 1;
        private const int TamanoPaginaMaximo = 200;
        private const int TamanoPaginaPorDefecto = 50;

        public async Task<PaginadoDto<ClienteResumenDto>> ObtenerTodosLosClientesAsync(
            int pagina = 1,
            int tamanoPagina = TamanoPaginaPorDefecto,
            string? busqueda = null)
        {
            // Normalizar parámetros: pagina ≥ 1 y tamaño dentro de rango seguro.
            if (pagina < 1) pagina = 1;
            if (tamanoPagina < TamanoPaginaMinimo) tamanoPagina = TamanoPaginaMinimo;
            if (tamanoPagina > TamanoPaginaMaximo) tamanoPagina = TamanoPaginaMaximo;

            // IQueryable<T> permite componer la consulta en SQL: el COUNT, el
            // filtro y el SKIP/TAKE viajan a MySQL en lugar de cargar todo a
            // memoria (que es lo que hacía la versión anterior con FindAsync).
            var query = _unitOfWork.Clientes.Query();

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                var termino = busqueda.Trim();
                // EF.Core traduce Contains a LIKE '%termino%' y MySQL en
                // collation utf8mb4_0900_ai_ci ya hace match accent/case-
                // insensitive, así que no necesitamos tocar el casing.
                query = query.Where(c =>
                    EF.Functions.Like(c.Dpi, $"%{termino}%") ||
                    EF.Functions.Like(c.Nombre, $"%{termino}%") ||
                    EF.Functions.Like(c.Apellido, $"%{termino}%") ||
                    EF.Functions.Like(c.Correo, $"%{termino}%") ||
                    _unitOfWork.Contadores.Query().Any(co =>
                        co.IdCliente == c.IdCliente &&
                        EF.Functions.Like(co.NumeroContador, $"%{termino}%")));
            }

            var totalRegistros = await query.CountAsync();
            var totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(totalRegistros / (double)tamanoPagina);

            // 1) Cargar la página de clientes (sin contadores todavía)
            var clientesPagina = await query
                .OrderBy(c => c.IdCliente)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(c => new
                {
                    c.IdCliente,
                    c.Dpi,
                    c.Nombre,
                    c.Apellido,
                    c.Correo
                })
                .ToListAsync();

            if (clientesPagina.Count == 0)
            {
                return new PaginadoDto<ClienteResumenDto>(
                    Pagina: pagina,
                    TamanoPagina: tamanoPagina,
                    TotalRegistros: totalRegistros,
                    TotalPaginas: totalPaginas,
                    Items: Array.Empty<ClienteResumenDto>());
            }

            // 2) Cargar los contadores de TODOS los clientes de la página en
            //    una sola query (evita N+1).
            var idsClientes = clientesPagina.Select(c => c.IdCliente).ToList();
            var contadores = await _unitOfWork.Contadores.Query()
                .Where(c => idsClientes.Contains(c.IdCliente))
                .Select(c => new
                {
                    c.IdCliente,
                    c.NumeroContador,
                    c.DireccionInmueble,
                    c.Estado
                })
                .ToListAsync();

            // 3) Cargar los recibos pendientes de TODOS los contadores de la
            //    página en una sola query y agruparlos por contador para
            //    sumarles el saldo.
            var numerosContador = contadores.Select(c => c.NumeroContador).ToList();
            var saldosPorContador = numerosContador.Count == 0
                ? new Dictionary<string, decimal>()
                : await _unitOfWork.Recibos.Query()
                    .Where(r => numerosContador.Contains(r.NumeroContador)
                             && r.Estado == ReciboEstado.Pendiente)
                    .GroupBy(r => r.NumeroContador)
                    .Select(g => new { Numero = g.Key, Saldo = g.Sum(r => r.SaldoPendiente) })
                    .ToDictionaryAsync(x => x.Numero, x => x.Saldo);

            var contadoresPorCliente = contadores
                .GroupBy(c => c.IdCliente)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(c => new ContadorBreveDto(
                            NumeroContador: c.NumeroContador,
                            DireccionInmueble: c.DireccionInmueble,
                            Estado: c.Estado,
                            SaldoPendiente: saldosPorContador.GetValueOrDefault(c.NumeroContador, 0m)))
                         .OrderBy(c => c.NumeroContador)
                         .ToList());

            var items = clientesPagina.Select(c =>
            {
                var contadoresCliente = contadoresPorCliente.GetValueOrDefault(
                    c.IdCliente,
                    new List<ContadorBreveDto>());
                return new ClienteResumenDto(
                    IdCliente: c.IdCliente,
                    Dpi: c.Dpi,
                    Nombre: c.Nombre,
                    Apellido: c.Apellido,
                    Correo: c.Correo,
                    CantidadContadores: contadoresCliente.Count,
                    SaldoTotalPendiente: contadoresCliente.Sum(x => x.SaldoPendiente),
                    Contadores: contadoresCliente);
            }).ToList();

            return new PaginadoDto<ClienteResumenDto>(
                Pagina: pagina,
                TamanoPagina: tamanoPagina,
                TotalRegistros: totalRegistros,
                TotalPaginas: totalPaginas,
                Items: items);
        }

        /// <summary>
        /// Genera un número de contador único reintentando hasta
        /// <see cref="MaxIntentosNumeroContador"/> veces si por casualidad
        /// colisiona con uno existente. Si se agotan los intentos lanza
        /// excepción para que el flujo aborte limpiamente.
        /// </summary>
        private async Task<string> GenerarNumeroContadorUnicoAsync()
        {
            for (int intento = 0; intento < MaxIntentosNumeroContador; intento++)
            {
                var candidato = GenerarNumeroContador();
                bool colision = await _unitOfWork.Contadores
                    .AnyAsync(c => c.NumeroContador == candidato);
                if (!colision)
                    return candidato;
            }

            throw new InvalidOperationException(
                $"No se pudo generar un número de contador único tras {MaxIntentosNumeroContador} intentos.");
        }

        private static string GenerarNumeroContador()
        {
            return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }

        // Alfabetos por categoría. Excluimos caracteres ambiguos (0/O, 1/l/I)
        // para que el password temporal sea fácil de dictar por teléfono y
        // de tipear sin confundirse en agencia.
        private const string LetrasMayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string LetrasMinusculas = "abcdefghijkmnpqrstuvwxyz";
        private const string Digitos = "23456789";
        private const string Simbolos = "!@#$%*?";
        private const int LongitudPasswordTemporal = 12;

        /// <summary>
        /// Genera un password temporal con entropía criptográfica. Garantiza
        /// al menos un carácter de cada categoría (mayúscula, minúscula,
        /// dígito y símbolo) y mezcla la posición de cada carácter usando
        /// <see cref="RandomNumberGenerator"/> para que no haya patrón
        /// derivable del DPI ni del momento de generación.
        /// </summary>
        private static string GenerarPasswordTemporalSeguro()
        {
            var caracteres = new char[LongitudPasswordTemporal];

            caracteres[0] = LetrasMayusculas[RandomNumberGenerator.GetInt32(LetrasMayusculas.Length)];
            caracteres[1] = LetrasMinusculas[RandomNumberGenerator.GetInt32(LetrasMinusculas.Length)];
            caracteres[2] = Digitos[RandomNumberGenerator.GetInt32(Digitos.Length)];
            caracteres[3] = Simbolos[RandomNumberGenerator.GetInt32(Simbolos.Length)];

            const string todos = LetrasMayusculas + LetrasMinusculas + Digitos + Simbolos;
            for (int i = 4; i < LongitudPasswordTemporal; i++)
            {
                caracteres[i] = todos[RandomNumberGenerator.GetInt32(todos.Length)];
            }

            // Fisher–Yates con RandomNumberGenerator para mezclar las posiciones
            // fijas (mayúscula, minúscula, dígito, símbolo) con el resto y que
            // un atacante no pueda asumir la categoría por índice.
            for (int i = caracteres.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
            }

            return new string(caracteres);
        }
    }
}
