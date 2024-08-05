// Property Provider
// Copyright (C) 2024 Pedro Sobota
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Text;
using static System.Console;
using static System.Environment;
using System.Text.RegularExpressions;

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class Utils {
    // `SingleOrDefault` with inline null checking
    
    public static bool SingleGet<T>(
        this IEnumerable<T> coll, 
        Predicate<T> pred,
        out T? elem
    ) {
        elem = coll.SingleOrDefault(x => pred(x));
        return elem != null;
    }
    
    public static TO CheckEmpty<TI, TO>(
        this IEnumerable<TI> coll, 
        Func<IEnumerable<TI>, TO> nonEmpty, 
        TO empty
    ) =>
        coll.Any() ? nonEmpty(coll) : empty;
    
    public static string Combine(this string path1, string path2) =>
        Path.Combine(path1, path2);
    
    public static string Join<T>(this IEnumerable<T> list, string separator) => 
        string.Join(separator, list);
        
    public static bool Match(this string str, string regex, out Match match) {
        match = Regex.Match(str, regex);
        return match.Success;
    }
    
    public static bool In<T>(this T t, params T[] arr) => arr.Contains(t);
 
    public static bool NegateIf(this bool a, bool b) => b ? !a : a;
    
    public static IEnumerable<string> Strs<T>(this IEnumerable<T> objs) =>
        objs.Select(x => x.ToString() ?? "");
        
    public static IEnumerable<string> Quoted(
        this IEnumerable<string> strings
    ) => 
        strings.Select(s => $"'{s}'");
    
    public static void WriteLines(
        this TextWriter tw,
        IEnumerable<string> lns
    ) => WriteLines(lns, tw);
    
    public static void WriteLines(
        IEnumerable<string> lns,
        TextWriter? tw = null
    ) {
        foreach (string ln in lns) (tw ?? Out).WriteLine(ln);
    }

    public static bool LongArgExists(
        this string[] args, 
        string name
    ) => args.Any(a => a == $"--{name}");

    public static bool ShortArgExists(
        this string[] args, 
        char letter
    ) => args.Any(a => a == $"-{letter}");
    
    /// <summary>
    /// Tries to get the argument in the format: '--[argname]'.
    /// </summary>
    /// <param name="twoDashes">If false, the format is '-[argname]'</param>
    public static bool TryGetArgv(
        this string[] args, 
        string name, 
        out string argv,
        bool twoDashes = true
    ) {
        argv = null!;
        int pos = -1;
        for (int i = 0; i < args.Length; i++)
            if (args[i] == $"{(twoDashes ? "--" : "-")}{name}") { pos = i; break; }
        if (pos == -1) return false;
        if (pos == args.Length - 1) return false;
        if (args[pos + 1].StartsWith(twoDashes ? "--" : "-")) return false;
        argv = args[pos + 1];
        return true;
    }
}

// https://stackoverflow.com/a/5073144/38234
public class Grouping<TKey, TElement> : 
    List<TElement>, IGrouping<TKey, TElement> 
{
    public Grouping(TKey key, IEnumerable<TElement> collection)
        : base(collection) => Key = key;
    public TKey Key { get; }
}
