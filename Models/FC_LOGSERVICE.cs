using System;
using System.Collections.Generic;

namespace TrafficControl.Tasas.Api.Models;

public partial class FC_LOGSERVICE
{
    public long LOGSERVICE_ID { get; set; }

    public string? LOGSERVICE_FUNCION { get; set; }

    public string? LOGSERVICE_MENSAJE { get; set; }

    public DateTime? LOGSERVIE_FECHA { get; set; }

    public string? LOGSERVICE_TIME { get; set; }

    public string? LOGSERVICE_PARAM { get; set; }

    public bool? LOGSERVICE_ERROR { get; set; }
}
