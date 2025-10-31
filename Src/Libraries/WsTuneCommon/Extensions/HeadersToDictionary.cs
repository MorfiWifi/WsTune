// using Microsoft.Extensions.Primitives;
//
// namespace WsTuneCommon.Extensions;
//
// public static class HeadersToDictionary
// {
//     public static Dictionary<string, string?[]> ToDict (this IEnumerable<KeyValuePair<string, StringValues>> headers)
//     {
//         return headers.ToDictionary(x => x.Key, x => x.Value.ToArray());
//     }
//     
//     public static Dictionary<string, string?[]> ToDict (this IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
//     {
//         return headers.ToDictionary(x => x.Key, x => x.Value.ToArray());
//     }
//     
// }