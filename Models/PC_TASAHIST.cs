using System;
using System.Collections.Generic;

namespace TrafficControl.Tasas.Api.Models;

public partial class PC_TASAHIST
{
    public long TS_ID { get; set; }

    public string TS_IDBC { get; set; } = null!;

    public DateTime? TS_FECHACREA { get; set; }

    public DateTime? TS_FECHAUSO { get; set; }

    public decimal? TS_VALOR { get; set; }

    public string? TS_ESTADO { get; set; }

    public DateTime? TS_FECHA_DESDE { get; set; }

    public DateTime? TS_FECHA_HASTA { get; set; }

    public int? TS_IDTRANS { get; set; }

    public string? TS_IP_MARCA { get; set; }

    public DateTime? TS_FECHAENTRYSTATION { get; set; }

    public DateTime? TS_FECHAPAYSTATION { get; set; }

    public DateTime? TS_FECHAEXITSTATION { get; set; }

    public DateTime? TS_FECHAMAXSALIDA { get; set; }

    public int? TS_IDENTRYSTATION { get; set; }

    public int? TS_IDPAYSTATION { get; set; }

    public int? TS_IDEXITSTATION { get; set; }

    public string? TS_TIPO_PASE { get; set; }

    public int? TS_IDCOOP { get; set; }
}
