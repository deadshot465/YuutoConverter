namespace YuutoConverter.Types

open System.Collections.Generic
open System.Text.Json.Serialization

type ExchangeRateAPIResponse = {
    success: bool
    timestamp: uint64
    [<JsonPropertyName("base")>]
    baseValue: string
    date: string
    rates: Dictionary<string, double>
}