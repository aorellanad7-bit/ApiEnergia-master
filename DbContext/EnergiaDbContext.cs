using ApiEnergia.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiEnergia.DbContext
{
    public class EnergiaDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public EnergiaDbContext(DbContextOptions<EnergiaDbContext> options) : base(options) { }

        public DbSet<PagosProcesados> PagosProcesados { get; set; }
        public DbSet<ReciboLuz> ReciboLuz { get; set; }
        public DbSet<LecturaContador> LecturaContador { get; set; }
        public DbSet<ClienteLuz> ClienteLuz { get; set; }
        public DbSet<ContadorEnergia> ContadorEnergia { get; set; }
        public DbSet<UsuarioAccesoEnergia> Accesos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── pagos_procesados ──────────────────────────────────────────────
            modelBuilder.Entity<PagosProcesados>(entity =>
            {
                entity.ToTable("pagos_procesados");
                entity.HasKey(e => e.IdPago);
                entity.Property(e => e.IdPago)
                      .HasColumnName("id_pago")
                      .ValueGeneratedOnAdd();
                entity.Property(e => e.IdRecibo)
                      .HasColumnName("id_recibo")
                      .IsRequired();
                entity.Property(e => e.NumeroContador)
                      .HasColumnName("numero_contador")
                      .HasMaxLength(30)
                      .IsRequired();
                entity.Property(e => e.Monto)
                      .HasColumnName("monto")
                      .HasColumnType("decimal(10,2)")
                      .IsRequired();
                entity.Property(e => e.FechaCobro)
                      .HasColumnName("fecha_cobro")
                      .IsRequired();
                entity.Property(e => e.CanalPago)
                      .HasColumnName("canal_pago")
                      .HasMaxLength(30)
                      .IsRequired();
                entity.Property(e => e.CodigoAutorizacionBanco)
                      .HasColumnName("codigo_autorizacion_banco")
                      .HasMaxLength(50);
                entity.HasOne(e => e.Recibo)
                      .WithMany(r => r.Pagos)
                      .HasForeignKey(e => e.IdRecibo);
            });

            // ── recibo_luz ────────────────────────────────────────────────────
            modelBuilder.Entity<ReciboLuz>(entity =>
            {
                entity.ToTable("recibo_luz");
                entity.HasKey(e => e.IdRecibo);
                entity.Property(e => e.IdRecibo)
                      .HasColumnName("id_recibo")
                      .ValueGeneratedOnAdd();
                entity.Property(e => e.LecturaContadorId)
                      .HasColumnName("id_lectura")
                      .IsRequired();
                entity.Property(e => e.NumeroContador)
                      .HasColumnName("numero_contador")
                      .HasMaxLength(30)
                      .IsRequired();
                entity.Property(e => e.MontoTotal)
                      .HasColumnName("monto_total")
                      .HasColumnType("decimal(10,2)")
                      .IsRequired();
                entity.Property(e => e.SaldoPendiente)
                      .HasColumnName("saldo_pendiente")
                      .HasColumnType("decimal(10,2)")
                      .IsRequired();
                entity.Property(e => e.FechaEmision)
                      .HasColumnName("fecha_emision")
                      .IsRequired();
                entity.Property(e => e.Estado)
                      .HasColumnName("estado")
                      .HasConversion<string>()
                      .IsRequired();
                entity.HasOne(e => e.LecturaContador)
                      .WithMany()
                      .HasForeignKey(e => e.LecturaContadorId);
            });

            // ── lectura_contador ─────────────────────────────────────────────
            modelBuilder.Entity<LecturaContador>(entity =>
            {
                entity.ToTable("lectura_contador");
                entity.HasKey(e => e.IdLectura);
                entity.Property(e => e.IdLectura)
                      .HasColumnName("id_lectura")
                      .ValueGeneratedOnAdd();
                entity.Property(e => e.NumeroContador)
                      .HasColumnName("numero_contador")
                      .HasMaxLength(30)
                      .IsRequired();
                entity.Property(e => e.KilovatiosConsumidos)
                      .HasColumnName("kilovatios_consumidos")
                      .IsRequired();
                entity.Property(e => e.FechaLectura)
                      .HasColumnName("fecha_lectura")
                      .IsRequired();
            });

            // ── cliente_luz ───────────────────────────────────────────────────
            modelBuilder.Entity<ClienteLuz>(entity =>
            {
                entity.ToTable("cliente_luz");
                entity.HasKey(e => e.IdCliente);
                entity.Property(e => e.IdCliente)
                      .HasColumnName("id_cliente")
                      .ValueGeneratedOnAdd();
                entity.Property(e => e.Dpi)
                      .HasColumnName("dpi")
                      .HasMaxLength(20)
                      .IsRequired();
                entity.Property(e => e.Nombre)
                      .HasColumnName("nombre")
                      .HasMaxLength(100)
                      .IsRequired();
                entity.Property(e => e.Apellido)
                      .HasColumnName("apellido")
                      .HasMaxLength(100)
                      .IsRequired();
                entity.Property(e => e.Correo)
                      .HasColumnName("correo")
                      .HasMaxLength(150)
                      .IsRequired();
            });

            // ── contador_energia ─────────────────────────────────────────────
            modelBuilder.Entity<ContadorEnergia>(entity =>
            {
                entity.ToTable("contador_energia");
                entity.HasKey(e => e.NumeroContador);
                entity.Property(e => e.NumeroContador)
                      .HasColumnName("numero_contador")
                      .HasMaxLength(30)
                      .IsRequired();
                entity.Property(e => e.IdCliente)
                      .HasColumnName("id_cliente")
                      .IsRequired();
                entity.Property(e => e.DireccionInmueble)
                      .HasColumnName("direccion_inmueble")
                      .HasMaxLength(255)
                      .IsRequired();
                entity.Property(e => e.FechaInstalacion)
                      .HasColumnName("fecha_instalacion")
                      .IsRequired();
                entity.Property(e => e.Estado)
                      .HasColumnName("estado")
                      .HasMaxLength(20)
                      .IsRequired();
                entity.HasOne(e => e.Cliente)
                      .WithMany(c => c.Contadores)
                      .HasForeignKey(e => e.IdCliente);
            });

            modelBuilder.Entity<UsuarioAccesoEnergia>(entity =>
            {
                entity.ToTable("usuario_acceso_energia");
                entity.HasKey(e => e.IdUsuario);
                entity.Property(e => e.IdUsuario)
                      .HasColumnName("id_usuario")
                      .ValueGeneratedOnAdd();
                entity.Property(e => e.IdCliente)
                      .HasColumnName("id_cliente")
                      .IsRequired();
                entity.Property(e => e.NombreUsuario)
                      .HasColumnName("nombre_usuario")
                      .HasMaxLength(50)
                      .IsRequired();
                entity.Property(e => e.PasswordHash)
                      .HasColumnName("password_hash")
                      .HasMaxLength(255)
                      .IsRequired();
                entity.Property(e => e.Rol)
                      .HasColumnName("rol")
                      .HasMaxLength(30)
                      .IsRequired();
            });
        }
    }
}
