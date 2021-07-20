namespace YuutoConverter.Types

open System.Collections.Immutable

type ConversionTable = {
    length: ImmutableDictionary<string, ImmutableDictionary<string, double>>
    weight: ImmutableDictionary<string, ImmutableDictionary<string, double>>
    temperature: ImmutableDictionary<string, ImmutableDictionary<string, double>>
}