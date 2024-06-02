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
    
    public static string Combine(this string path1, string path2) =>
        Path.Combine(path1, path2);
        
    // Gets width and height of a multiline string
    
    public static (int W, int H) GetStrWidthHeight(string multiline) {
        string[] spl = multiline.Split(NewLine);
        return (spl.Max(x => x.Length), spl.Length);
    }
    
    public static string Join<T>(this IEnumerable<T> list, string separator) => 
        string.Join(separator, list);
        
    public static bool Match(this string str, string regex, out Match match) {
        match = Regex.Match(str, regex);
        return match.Success;
    }
    
    public static bool In<T>(this T t, params T[] arr) => arr.Contains(t);
    
    public static void Times(this int x, Action a) {
        for (int i = 0; i < x; i++)
            a();
    }
    
    public static IEnumerable<string> Strs<T>(this IEnumerable<T> objs) =>
        objs.Select(x => x.ToString() ?? "");
        
    public static IEnumerable<string> Quoted(
        this IEnumerable<string> strings
    ) => 
        strings.Select(s => $"'{s}'");

    public static bool LongArgExists(
        this string[] args, 
        string name
    ) => args.Any(a => a == $"--{name}");
}

// https://stackoverflow.com/a/5073144/38234
public class Grouping<TKey, TElement> : 
    List<TElement>, IGrouping<TKey, TElement> 
{
    public Grouping(TKey key) : base() => Key = key;
    public Grouping(TKey key, int capacity) : base(capacity) => Key = key;
    public Grouping(TKey key, IEnumerable<TElement> collection)
        : base(collection) => Key = key;
    public TKey Key { get; }
}