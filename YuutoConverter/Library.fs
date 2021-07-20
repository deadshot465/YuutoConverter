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
    /// Available length units for conversion.
    let AvailableLengthes = [| "km"; "m"; "cm"; "in"; "ft"; "mi"; "au" |]
    /// Available weight units for conversion.
    let AvailableWeights = [| "kg"; "g"; "lb" |]
    /// Available temperature units for conversion.
    let AvailableTemperatures = [| "c"; "f"; "k" |]
    let private conversionTable = JsonSerializer.Deserialize<ConversionTable>(File.ReadAllText("Assets/convert.json"))
    let private baseUrl = "http://api.exchangeratesapi.io/v1/latest"
    
    /// [exchangeCurrency source target token httpClient] will call remote API and convert source currency to target currency using specified token and HttpClient.
    /// Throws exception when HTTP request fails, or unable to find the denoted currency type to fulfill the conversion.
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
                return responseResult.rates.[target] / responseResult.rates.[source]
    }
    
    /// [checkCompatible converterType] will check units to be converted are compatible with each other or not.
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
        
    /// [fixLetterCases converterType] will destruct converterType and forcibly turn letters to valid cases. 
    [<Pure>]
    let private fixLetterCases converterType =
        match converterType with
        | Length (source, target) -> Length (source.ToLower(), target.ToLower())
        | Weight (source, target) -> Weight (source.ToLower(), target.ToLower())
        | Temperature (source, target) -> Temperature (source.ToLower(), target.ToLower())
        | Currency (source, target) -> Currency (source.ToUpper(), target.ToUpper())
    
    /// [computeTemperature source target amount] will convert the amount in source temperature unit to target temperature unit.
    /// Throws exception when specified source temperature unit is unsupported/unknown.
    let private computeTemperature (source: string) (target: string) (amount: double) =
        match source with
        | "c" ->
            let adjustment =
                match target with
                | "f" -> 32.0
                | "k" -> 273.15
                | _ -> 0.0
            (amount / conversionTable.temperature.[source].[target]) + adjustment
        | "f" ->
            let adjustment =
                match target with
                | "c" -> -32.0
                | "k" -> 459.67
                | _ -> 0.0
            (amount + adjustment) / conversionTable.temperature.[source].[target]
        | "k" ->
            let adjustment =
                match target with
                | "c" -> -273.15
                | "f" -> -459.67
                | _ -> 0.0
            (amount / conversionTable.temperature.[source].[target]) + adjustment
        | _ ->
            failwith "Unsupported conversion between temperatures."
            0.0
    
    /// [Convert converterType amount token httpClient] will destruct converterType and convert the specified amount from corresponding source units to target units.
    /// In case of converting between currencies, the amount in source currency will be converted to target currency using specified token and HttpClient.
    /// Throws exception when source unit and target unit are incompatible, or no token is specified when converting currencies.
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
                | Temperature (source, target) -> return computeTemperature source target amount
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