using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TrafficControl.Tasas.Api.Models;

namespace TrafficControl.Tasas.Api.Data;

public partial class TrafficControlDbContext : DbContext
{
    public TrafficControlDbContext(DbContextOptions<TrafficControlDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<FC_LOGSERVICE> FC_LOGSERVICE { get; set; }

    public virtual DbSet<PC_TASA> PC_TASA { get; set; }

    public virtual DbSet<PC_TASAHIST> PC_TASAHIST { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FC_LOGSERVICE>(entity =>
        {
            entity.HasKey(e => e.LOGSERVICE_ID);

            entity.Property(e => e.LOGSERVICE_ERROR).HasDefaultValue(false);
            entity.Property(e => e.LOGSERVICE_FUNCION).HasMaxLength(100);
            entity.Property(e => e.LOGSERVICE_MENSAJE).HasColumnType("text");
            entity.Property(e => e.LOGSERVICE_PARAM).HasColumnType("text");
            entity.Property(e => e.LOGSERVICE_TIME)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.LOGSERVIE_FECHA)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<PC_TASA>(entity =>
        {
            entity.HasKey(e => e.TS_IDBC).HasName("PK_OC_TASA");

            entity.Property(e => e.TS_IDBC)
                .HasMaxLength(70)
                .IsUnicode(false);
            entity.Property(e => e.TS_ESTADO)
                .HasMaxLength(1)
                .IsUnicode(false)
                .HasDefaultValueSql("((0))")
                .IsFixedLength()
                .HasComment("0 CREADO / 1 USADA / 2 CADUCADA");
            entity.Property(e => e.TS_FECHACREA)
                .HasComment("FECHA CREACION TASA")
                .HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAENTRYSTATION).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAEXITSTATION).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAMAXSALIDA).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAPAYSTATION)
                .HasDefaultValue(new DateTime(1900, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified))
                .HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAUSO)
                .HasComment("FECHA USO DE LA TASA")
                .HasColumnType("datetime");
            entity.Property(e => e.TS_FECHA_DESDE)
                .HasComment("FECHA INICIO VIGENCIA")
                .HasColumnType("datetime");
            entity.Property(e => e.TS_FECHA_HASTA)
                .HasComment("FECHA FIN VIGENCIA")
                .HasColumnType("datetime");
            entity.Property(e => e.TS_ID).ValueGeneratedOnAdd();
            entity.Property(e => e.TS_IDTRANS).HasComment("ID DE TRANSACCION - NULL CUANDO VIENE DE OTROS SISTEMAS EJ. SIAT3000");
            entity.Property(e => e.TS_IP_MARCA)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TS_TIPO_PASE)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.TS_VALOR).HasColumnType("money");
        });

        modelBuilder.Entity<PC_TASAHIST>(entity =>
        {
            entity.HasKey(e => e.TS_IDBC).HasName("PK_OC_TASAHIST");

            entity.Property(e => e.TS_IDBC)
                .HasMaxLength(70)
                .IsUnicode(false);
            entity.Property(e => e.TS_ESTADO)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.TS_FECHACREA).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAENTRYSTATION).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAEXITSTATION).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAMAXSALIDA).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAPAYSTATION).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHAUSO)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TS_FECHA_DESDE).HasColumnType("datetime");
            entity.Property(e => e.TS_FECHA_HASTA).HasColumnType("datetime");
            entity.Property(e => e.TS_IP_MARCA)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TS_TIPO_PASE)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.TS_VALOR).HasColumnType("money");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
