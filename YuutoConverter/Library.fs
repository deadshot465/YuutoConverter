namespace YuutoConverter

open System.Diagnostics.Contracts
open System.IO
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open YuutoConverter.Types

type ConverterType =
    Length of (string * string)
    | Weight of (string * string)
    | Temperature of (string * string)
    | Currency of (string * string)

module Converter =
    let private conversionTable = JsonSerializer.Deserialize<ConversionTable>(File.ReadAllText("Assets/convert.json"))
    let private baseUrl = "http://api.exchangeratesapi.io/v1/latest"
    
    let private exchangeCurrency (source: string) (target: string) token (httpClient: HttpClient) = async {
        let requestUrl = $"{baseUrl}?access_key={token}&symbols={source},{target}"
        let! response = httpClient.GetAsync(requestUrl) |> Async.AwaitTask
        if not response.IsSuccessStatusCode then
            failwith $"Unable to send HTTP request to the API server.\nHTTP status code: {response.StatusCode}\nMessage: {response.ReasonPhrase}"
            return (double)0.0
        else
            let! dataString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            let responseResult = JsonSerializer.Deserialize<ExchangeRateAPIResponse>(dataString)
            if not (responseResult.rates.ContainsKey(source)) || not (responseResult.rates.ContainsKey(target)) then
                failwith "Unable to find corresponding currency type."
                return 0.0
            else
                return (double)1.0 / (responseResult.rates.[source] * responseResult.rates.[target])
    }
    
    [<Pure>]
    let private checkCompatible converterType =
        match converterType with
        | Length (source, target) ->
            if conversionTable.length.ContainsKey(source) then conversionTable.length.[source].ContainsKey(target) else false
        | Weight (source, target) ->
            if conversionTable.weight.ContainsKey(source) then conversionTable.weight.[source].ContainsKey(target) else false
        | Temperature (source, target) ->
            if conversionTable.temperature.ContainsKey(source) then conversionTable.temperature.[source].ContainsKey(target) else false
        | _ -> true
        
    [<Pure>]
    let fixLetterCases converterType =
        match converterType with
        | Length (source, target) -> Length (source.ToLower(), target.ToLower())
        | Weight (source, target) -> Weight (source.ToLower(), target.ToLower())
        | Temperature (source, target) -> Temperature (source.ToLower(), target.ToLower())
        | Currency (source, target) -> Currency (source.ToUpper(), target.ToUpper())
    
    let Convert converterType amount (token: string option) (httpClient: HttpClient option) =
        let converterTypeCaseFixed = fixLetterCases converterType
        if not (checkCompatible converterTypeCaseFixed) then
            failwith "Input type and output type are incompatible."
            Task.FromResult((double)0.0)
        else
            let conversionResult = async {
                match converterTypeCaseFixed with
                | Length (source, target) -> return amount / conversionTable.length.[source].[target]
                | Weight (source, target) -> return amount / conversionTable.weight.[source].[target]
                | Temperature (source, target) -> return amount / conversionTable.temperature.[source].[target]
                | Currency (source, target) ->
                    let client = match httpClient with
                                 | Some(c) -> c
                                 | None -> new HttpClient()
                    let actualToken = match token with
                                      | Some(t) -> t
                                      | None -> failwith "An access token has to be provided when converting currency."
                    let! result = exchangeCurrency source target actualToken client
                    return amount * result
            }
            
            conversionResult |> Async.StartAsTask