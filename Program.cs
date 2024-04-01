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

using System.IO;
using static System.Console;
using static System.Environment;
using static System.Text.Encoding;
using static Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

#region Data

Dictionary<string, List<Symb>> classes = new();
Dictionary<(string, string), List<(Symb, Symb)>> relations = new();

void LoadFile(string pth, out LoadFileStats stats, TextWriter? dbg = null) {
    classes.Clear();
    relations.Clear();
    Symbols.Clear();
    
    string[] lns = File.ReadAllLines(pth);
    
    stats = new();
    
    for (int i = 0; i < lns.Length; i++) {
        dbg?.Write($"{i + 1} ");
        string ln = lns[i].Trim();
        if (ln.Length == 0 || ln.StartsWith("//") || 
            ln.StartsWith("#")) continue;
        string[] spl = ln.Split(",");
        if (spl.Length != 2) throw new ParseException(
            $"Line {i + 1}: 2 terms expected, but {spl.Length} found");
        string a = spl[0].Trim();
        string b = spl[1].Trim();
        bool aIsQualified = a.Contains('.');
        bool bIsQualified = b.Contains('.');
        if (aIsQualified != bIsQualified) 
            throw new ParseException($"Line {i}, term {
                (aIsQualified ? "2" : "1")}: missing qualification");
        if (!a.Contains('.')) {
            // Class element
            string clss = a;
            if (!classes.ContainsKey(clss)) {
                classes.Add(clss, new());
                stats.ClassesConstructed++;
            }
            string symbStr = spl[1].Trim();
            Symb symb = Symbols.Create(clss, symbStr);
            classes[clss].Add(symb);
            stats.ClassElementsAdded++;
        }
        else {
            // Relation element
            Symb EvalTerm(string term, int tno) {
                string[] sp = term.Split('.');
                if (sp.Length != 2) throw new ParseException(
                    $"Line {i + 1}, term {tno}: two components expected, but {
                        sp.Length} found");
                string clss = sp[0].Trim();
                if (!classes.ContainsKey(clss)) 
                    throw new ParseException($"Line {i + 1}, term {tno}: " + 
                        $"undeclared class '{clss}'");
                // Ok, construct a symbol
                string el = sp[1].Trim();
                Symb symb = 
                    classes[clss].SingleGet(x => x.Name == el, out Symb sym) ?
                    sym :
                    Symbols.Create(clss, el);
                return symb;
            }
            Symb ra = EvalTerm(a, 1);
            Symb rb = EvalTerm(b, 2);
            if (ra.Class == rb.Class) {
                throw new ParseException($"Line {i + 1}: a class is not " + 
                    "allowed to relate to itself");
            }
            else if (!relations.ContainsKey((ra.Class, rb.Class))) {
                relations.Add((ra.Class, rb.Class), new());
                // Also add the inverse relation
                relations.Add((rb.Class, ra.Class), new());
                stats.RelationsDefined += 2;
            }
            void AddToRelation((string, string) def, (Symb A, Symb B) el) {
                var rel = relations[def];
                if (rel.Contains(el))
                    throw new ParseException($"Line {i + 1}: relation '{
                        el.A.Class}, {el.B.Class}' already contains an element '{
                        el.A.Name}, {el.B.Name}'");
                rel.Add(el);
            }
            AddToRelation((ra.Class, rb.Class), (ra, rb));
            // Not in the local method because it's an out parameter
            stats.RelationElementsDefined++;
            // Populate inverse relation
            AddToRelation((rb.Class, ra.Class), (rb, ra));
            stats.RelationElementsDefined++;
        }
    }
    dbg?.WriteLine();
}

#endregion Data

#region Constants

string outDir = GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    .Combine("PropertyProvider");
if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
string outPath = outDir.Combine("PropertyProviderOut.txt");

#endregion

#region State

State state = State.Main;
int currPage = 1;
(string, string) currRelation = default!;
(int, int)? clearArea = null;
Symb? currReferenceA = null!;
bool currDissimilarity = false;
bool currShowEquality = false;
string? autoInput = null;

#endregion State

#region Loop

WriteLine("PropertyProvider v0.1");
WriteLine();

while (true) {
    int cursorTop = CursorTop;
    string prompt = state switch {
        State.Main => "PP> ",
        State.Sub => "> "
    };
    Write(prompt);
    string inpt = autoInput ?? ReadLine();
    if (autoInput != null) { WriteLine(); autoInput = null; }
    int inptLen = inpt.Length + prompt.Length;
    if (state == State.Main) {
        if (inpt == "exit") return 0;
        try {
            ProcessMainCmd(inpt, out bool stateToSub);
            if (stateToSub) {
                CursorTop = cursorTop + 1;
                CursorLeft = 0;
            }
            else {
                WriteLine();
            }
        }
        catch (CommandException ex) {
            WriteLine(ex.Message);
            WriteLine();
        }
    }
    else if (state == State.Sub) {
        try {
            ProcessSubCmd(inpt, out bool stateToMain);
            if (!stateToMain) {
                CursorTop = cursorTop;
                CursorLeft = 0;
                Write(new string(' ', inptLen)); // Erase command
                CursorLeft = 0;
            }
            else {
                WriteLine();
            }
        }
        catch (CommandException ex) {
            WriteLine(ex.Message);
            WriteLine();
        }
    }
}

void ProcessMainCmd(string inpt, out bool stateToSub) {
    stateToSub = false;
    if (inpt == "help") {
        string ln = 
            "exit, outfile, classes, relations, [className], " + NewLine +
            "[className] v [className]" + NewLine +
            "[className] v [className] (!|!!|?|??) [classElement]";
        WriteLine(ln);
        clearArea = GetStrWidthHeight(ln);
    }
    else if (inpt == "outfile") {
        WriteLine(outPath);
    }
    else if (inpt.StartsWith("load ")) {
        string pth = inpt[5..];
        if (!File.Exists(pth)) 
            throw new CommandException($"The file '{pth}' was " + 
                "not found");
        try {
            LoadFile(pth, out LoadFileStats stats, dbg: null);
            
            WriteLine($"{stats.ClassesConstructed} classes constructed");
            WriteLine($"{stats.ClassElementsAdded} class elements added");
            WriteLine($"{stats.RelationsDefined} relations defined");
            WriteLine($"{stats.RelationElementsDefined} relation elements " + 
                "defined");
        }
        catch (ParseException ex) {
            WriteLine(ex.Message);
        }
    }
    else if (inpt == "classes") {
        WriteLine(classes.Keys.Join(", "));
    }
    else if (inpt == "relations") {
        WriteLine(relations.Keys.Join(", "));
    }
    else if (classes.ContainsKey(inpt)) {
        WriteLine(classes[inpt].Join(", "));
    }
    else if (inpt.Match("^([a-zA-Z]+) (#|x|v|vs|vs.) ([a-zA-Z]+)$", 
        out Match match)) 
    {
        string key = match.Groups[1].Value;
        string val = match.Groups[3].Value;
        if (key == val) 
            throw new CommandException("A class is not allowed to relate to " + 
                "itself");
        if (!classes.ContainsKey(key))
            throw new CommandException($"Class not found: {key}");
        if (!classes.ContainsKey(val))
            throw new CommandException($"Class not found: {val}");
        if (!relations.ContainsKey((key, val))) 
            throw new CommandException($"Relation not found: ({
                key}, {val})");
        currRelation = (key, val);
        currReferenceA = null;
        state = State.Sub;
        stateToSub = true;
        currPage = 1;
        autoInput = "show";
    }
    else if (inpt.Match("^([a-zA-Z]+) (#|x|v|vs|vs.) ([a-zA-Z]+) " + 
        "(!|!!|\\?|\\?\\?) ([a-zA-Z]+)$", out match)) 
    {
        string key = match.Groups[1].Value;
        string val = match.Groups[3].Value;
        string eq  = match.Groups[4].Value;
        string rfa = match.Groups[5].Value;
        if (key == val) 
            throw new CommandException("A class is not allowed to relate " + 
                "to itself");
        if (!classes.TryGetValue(key, out List<Symb> keyClass))
            throw new CommandException($"Class not found: {key}");
        if (!classes.ContainsKey(val))
            throw new CommandException($"Class not found: {val}");
        if (!relations.ContainsKey((key, val))) 
            throw new CommandException($"Relation not found: ({
                key}, {val})");
        if (!keyClass.SingleGet(x => x.Name == rfa, 
            out currReferenceA)) 
            throw new CommandException($"Element not found: {
                key}.{rfa}");
        currRelation = (key, val);
        currDissimilarity = eq.In("?", "??");
        currShowEquality = eq.In("!", "?");
        state = State.Sub;
        stateToSub = true;
        currPage = 1;
        autoInput = "show";
    }
    else throw new CommandException("Unrecognized command");
}

void ClearAreaIfAny() {
    if (clearArea == null) return;
    var (w, h) = clearArea.Value;
    CursorLeft = 0;
    h.Times(() => {
        Write(new string(' ', w));
        CursorLeft = 0;
        CursorTop++;
    });
    clearArea = null;
    CursorTop -= h;
}

void SkipAreaIfAny(bool eraseClearArea = true) {
    if (clearArea == null) return;
    var (_, h) = clearArea.Value;
    CursorTop += h;
    CursorLeft = 0;
    if (eraseClearArea) clearArea = null;
}

void ProcessSubCmd(string inpt, out bool stateToMain) {
    stateToMain = false;
    
    bool save = false;
    
    if (inpt == "help") {
        ClearAreaIfAny();
        string ln = "b|break, c|clear, pp, pn, pf, pl, p[pageNumber]";
        WriteLine(ln);
        clearArea = (ln.Length, 1);
        return;
    }
    else if (inpt.In("b", "break")) {
        SkipAreaIfAny();
        state = State.Main;
        stateToMain = true;
        return;
    }
    else if (inpt.In("c", "clear")) {
        ClearAreaIfAny();
        state = State.Main;
        stateToMain = true;
        return;
    }
    else if (inpt == "save") {
        SkipAreaIfAny(eraseClearArea: false);
        save = true;
    }
    else if (inpt.In("s", "show")) {
        int numPages = classes[currRelation.Item2].Count;
        currPage = 1;
    }
    else if (inpt.Match("^p([0-9]+|p|n|f|l)$", out Match match)) {
        int numPages = classes[currRelation.Item2].Count;
        
        string parm = match.Groups[1].Value;
        if (parm == "p") {
            if (currPage > 1) currPage--;
        }
        else if (parm == "n") {
            if (currPage < numPages) currPage++;
        }
        else if (parm == "f") {
            currPage = 1;
        }
        else if (parm == "l") {
            currPage = numPages;
        }
        else {
            int pg = int.Parse(parm);
            currPage = 
                (pg < 1) ? 1 :
                (pg > numPages) ? numPages :
                pg;
        }
    }
    else {
        SkipAreaIfAny();
        WriteLine("Command not recognized");
        return;
    }
    
    if (!relations.TryGetValue(currRelation, out List<(Symb, Symb)> rel)) 
        throw new CommandException($"Relation not found: {
            currRelation}");
    if (!classes.TryGetValue(currRelation.Item1, out List<Symb> keyClass)) 
        throw new CommandException($"Class not found: {
            currRelation.Item1}");
    if (!classes.TryGetValue(currRelation.Item2, out List<Symb> valClass)) 
        throw new CommandException($"Class not found: {
            currRelation.Item2}");
    
    // The presence of `currReferenceA` denotes it's a Scoring table, vs. a 
    // Relation table
    
    if (currReferenceA == null) {
        var t = CartesianJoin(
            keyClass, 
            valClass, 
            rel
        );
        
        var tableData = GetTableData(
            table: t,
            keyColumnName: currRelation.ToString(),
            columnsName: "",
            keyToString: k => k.Name,
            valueToString: v => v.Name,
            page: !save ? currPage : null
        );
        
        // The clear area shouldn't be changed when printing to a file
        if (!save) clearArea = (tableData.TotalWidth, tableData.TotalHeight);
        
        var lns = DrawTableBoolean(
            table: t,
            keyColumnName: currRelation.ToString(),
            columnsName: "",
            keyToString: k => k.Name,
            valueToString: v => v.Name,
            tableData: tableData,
            page: !save ? currPage : null,
            unicode: false
        );
        
        if (!save) foreach (string ln in lns) WriteLine(ln);
        else {
            File.AppendAllLines(outPath, lns);
        }
    }
    else {
        var t = ScoreGrouped(CartesianJoinScore(
            keyClass, 
            valClass, 
            rel, 
            currReferenceA,
            currDissimilarity
        ), showEquality: currShowEquality);
        
        string keyColName = currRelation.ToString();
        
        var tableData = GetTableData(
            table: t,
            keyColumnName: keyColName,
            columnsName: "",
            keyToString: k => k.A.Name,
            valueToString: v => v.Name,
            page: !save ? currPage : null
        );
        
        // The clear area shouldn't be changed when printing to a file
        if (!save) clearArea = (tableData.TotalWidth, tableData.TotalHeight);
        
        var lns = DrawTableBoolean(
            table: t,
            keyColumnName: keyColName,
            columnsName: "",
            keyToString: k => $"({k.Score}) {k.A.Name}",
            valueToString: v => v.Name,
            tableData: tableData,
            page: !save ? currPage : null,
            unicode: false
        );
        
        if (!save) foreach (string ln in lns) WriteLine(ln);
        else {
            File.AppendAllLines(outPath, lns);
        }
    }
}

#endregion Loop

#region Transformations

// For the non-scoring table

static IEnumerable<IGrouping<Symb, (Symb B, bool AHasB)>> CartesianJoin(
    IEnumerable<Symb> a, 
    IEnumerable<Symb> b,
    // The relation needs to be in correct order to match specific a and b
    IEnumerable<(Symb RelA, Symb RelB)> rel
) =>
    // Unconditional group join (simple Cartesian product)
    a.GroupJoin(
        b,
        x => true,
        y => true,
        (x, y) => (A: x, Bs: y.DefaultIfEmpty())
    )
    // Exploded, or otherwise it can't be joined on the relation
    .SelectMany(x => x.Bs.Select(b => (A: x.A, B: b)))
    // Left-joining relation
    .GroupJoin(
        rel,
        x => (x.A, x.B),
        y => (y.Item1, y.Item2),
        (x, y) => (A: x.A, B: x.B, AHasB: y.Any())
    )
    // Grouped
    .GroupBy(x => x.A, x => (x.B, x.AHasB));

// For the scoring table

static IEnumerable<CartesianJoinScoreData> CartesianJoinScore(
    IEnumerable<Symb> a, 
    IEnumerable<Symb> b, 
    IEnumerable<(Symb RelA, Symb RelB)> rel,
    Symb referenceA,
    bool dissimilarity
) {
    var @this = rel.Where(r => r.RelA.Equals(referenceA));
    
    CartesianJoinScoreData Projection(
        (Symb A, Symb B) x,
        IEnumerable<(Symb RelA, Symb RelB)> y,
        bool dissimilarity = false
    ) {
        bool aHasB = y.Any();
        bool referenceAHasB = @this!.Any(z => z.RelB.Equals(x.B));
        bool aHasBEqualsReferenceAHasB = aHasB.Equals(referenceAHasB);
        int score = aHasBEqualsReferenceAHasB == !dissimilarity ? 1 : 0;
        return new(
            x.A,
            x.B,
            aHasB,
            referenceAHasB,
            aHasBEqualsReferenceAHasB,
            score
        );
    }
    
    // Unconditional group join (simple Cartesian product)
    return a.GroupJoin(
        b,
        x => true,
        y => true,
        (x, y) => (A: x, Bs: y.DefaultIfEmpty())
    )
    // Exploded, or otherwise it can't be joined on the relation
    .SelectMany(x => x.Bs.Select(b => (A: x.A, B: b)))
    // Left-joining relation
    .GroupJoin(
        rel,
        x => (x.A, x.B),
        y => (y.Item1, y.Item2),
        (x, y) => Projection(x, y, dissimilarity)
    );
}

// Scoring final grouping

static IEnumerable<IGrouping<(Symb A, int Score), (Symb B, bool AHasB)>> 
ScoreGrouped(
    IEnumerable<CartesianJoinScoreData> data, 
    bool dissimilarity = false,
    bool showEquality = false
) =>
    from g in data.GroupBy(x => x.A)
    let scr = g.Sum(x => x.Score)
    orderby scr descending
    select new Grouping<(Symb A, int Score), (Symb B, bool AHasB)>(
        (g.Key, scr), 
        g.Select(x => (
            x.B, 
            !showEquality ? 
                (!dissimilarity ? x.AHasB : !x.AHasB) : 
                (!dissimilarity ? x.AHasBEqualsReferenceAHasB : 
                    !x.AHasBEqualsReferenceAHasB)
        )));

#endregion Transformations

#region Table drawing

static TableData GetTableData<TKey, TValue>(
    IEnumerable<IGrouping<TKey, (TValue, bool)>> table,
    string keyColumnName,
    string columnsName,
    Func<TKey, string> keyToString,
    Func<TValue, string> valueToString,
    int? page = null,
    TextWriter? dbg = null
) {
    int valTotalColCount = table.Max(s => s.Count());
    dbg?.WriteLine($"valTotalColCount is {valTotalColCount}");
    int keyColWidth = Math.Max(
        table.Max(s => keyToString(s.Key)?.Length ?? 0), 
        keyColumnName.Length);
    if (page != null && page.Value < 1) page = 1;
    else if (page != null && page.Value > valTotalColCount) 
        page = valTotalColCount;
    dbg?.WriteLine($"Page is {
        (page != null ? page.Value.ToString() : "null")}");
    int valInitialCol = page != null ? page.Value : 1;
    dbg?.WriteLine($"valInitialCol is {valInitialCol}");
    int valFinalCol = page != null ? page.Value + 1 : valTotalColCount + 1;
    dbg?.WriteLine($"valFinalCol is {valFinalCol}");
    int valColWidth = table.Max(s =>
        s.Any() ?
            s.Max(p => valueToString(p.Item1)?.Length ?? 0) :
            0
    );
    int totalWidth = 
        keyColWidth + 4 + 
        valColWidth * (valFinalCol - valInitialCol) +
        3 * (valFinalCol - valInitialCol);
    dbg?.WriteLine($"totalWidth is {totalWidth}");
    // Includes the pager indicator < > if present
    int totalHeight = 4 + table.Count() + 
        (page != null ? 1 : 0);
    dbg?.WriteLine($"totalHeight is {totalHeight}");
    return new (
        valTotalColCount,
        keyColWidth,
        page,
        valInitialCol,
        valFinalCol,
        valColWidth,
        totalWidth,
        totalHeight
    );
}

static IEnumerable<string> DrawTableBoolean<TKey, TValue>(
    IEnumerable<IGrouping<TKey, (TValue, bool)>> table,
    string keyColumnName,
    string columnsName,
    Func<TKey, string> keyToString,
    Func<TValue, string> valueToString,
    // If not passed, it will be calculated
    TableData? tableData = null,
    int? page = null,
    bool unicode = false,
    TextWriter? dbg = null
) where TKey : notnull where TValue : notnull {
    var (
        valTotalColCount,
        keyColWidth,
        pageBounded,
        valInitialCol,
        valFinalCol,
        valColWidth,
        _,
        _
    ) = tableData ?? GetTableData(
        table,
        keyColumnName,
        columnsName,
        keyToString,
        valueToString,
        page,
        dbg: dbg
    );
    
    var sb = new StringBuilder();

    sb.Append(unicode ? "┌" : "+");
    for (int j = 0; j < keyColWidth + 2; j++)
        sb.Append(unicode ? "─" : "-");
    if (valFinalCol > 0)
        sb.Append(unicode ? "┬" : "+");
    else
        sb.Append(unicode ? "┐" : "+");
    for (int i = valInitialCol - 1; i < valFinalCol - 1; i++) {
        for (int j = 0; j < valColWidth + 2; j++)
            sb.Append(unicode ? "─" : "-");
        if (i < valFinalCol - 2) sb.Append(unicode ? "┬" : "+");
        else sb.Append(unicode ? "┐" : "+");
    }
    yield return sb.ToString();

    // Header

    sb.Clear();
    sb.Append(unicode ? "│" : "|");
    sb.Append($" {keyColumnName} ".PadRight(keyColWidth + 2, ' '));
    sb.Append(unicode ? "│" : "|");
    for (int i = valInitialCol - 1; i < valFinalCol - 1; i++) {
        sb.Append($"{columnsName} {i + 1}/{valTotalColCount}"
            .PadRight(valColWidth + 1, ' '));
        if (i < valFinalCol - 2) 
            sb.Append(unicode ? " │" : " |");
        else sb.Append(unicode ? " │" : " |");
    }
    yield return sb.ToString();

    sb.Clear();
    sb.Append(unicode ? "├" : "+");
    for (int j = 0; j < keyColWidth + 2; j++)
        sb.Append(unicode ? "─" : "-");
    if (valFinalCol > 0)
        sb.Append(unicode ? "┼" : "+");
    else
        sb.Append(unicode ? "┤" : "+");
    for (int i = valInitialCol - 1; i < valFinalCol - 1; i++) {
        for (int j = 0; j < valColWidth + 1; j++)
            sb.Append(unicode ? "─" : "-");
        sb.Append(unicode ? "─" : "-");
        if (i < valFinalCol - 2) sb.Append(unicode ? "┼" : "+");
        else sb.Append(unicode ? "┤" : "+");
    }
    yield return sb.ToString();

    // Body
    
    // "Symbols" here refers to the character representation of values.
    
    // Initialized to the first column collection to check consistency across 
    // all rows.
    TValue[] colCheck = null!;
    
    foreach (var kv in table) {
        sb.Clear();
        sb.Append(unicode ? "│ " : "| ");
        sb.Append((keyToString(kv.Key) ?? "").PadRight(keyColWidth, ' '));
        sb.Append(unicode ? " │ " : " | ");
        // Columns
        var cols = kv.Order().ToArray();
        var colNames = cols.Select(x => x.Item1).ToArray();
        if (colCheck != null && !colNames.SequenceEqual(colCheck)) 
            throw new ArgumentException($"Inconsistent columns: expected{
                NewLine}{colCheck.Strs().Quoted().Join(", ")} but got{
                NewLine}{cols.Strs().Quoted().Join(",  ")}");
        if (colCheck == null) colCheck = colNames;
        int i = valInitialCol - 1;
        
        var colsE = page != null ?
            cols.Skip(valInitialCol - 1).Take(1) :
            cols;
        
        // If there is a page, print a single page
        foreach (var (val, marked) in colsE) {
            if (marked) {
                sb.Append(valueToString(val).PadRight(valColWidth + 1, ' '));
                sb.Append(unicode ? "│" : "|");
                if (
                    i < cols.Length - 1 ||
                    cols.Length < valFinalCol
                ) sb.Append(" ");
            }
            else {
                sb.Append("".PadLeft(valColWidth, ' '));
                sb.Append(unicode ? " │" : " |");
                if (i < valFinalCol - 1) sb.Append(" ");
            }
            i++;
        }
        yield return sb.ToString();
    }

    sb.Clear();
    sb.Append(unicode ? "└" : "+");
    for (int j = 0; j < keyColWidth + 2; j++)
        sb.Append(unicode ? "─" : "-");
    if (valFinalCol > 0)
        sb.Append(unicode ? "┴" : "+");
    else
        sb.Append(unicode ? "┘" : "+");
    for (int i = valInitialCol - 1; i < valFinalCol - 1; i++) {
        for (int j = 0; j < valColWidth + 1; j++)
            sb.Append(unicode ? "─" : "-");
        sb.Append(unicode ? "─" : "-");
        if (i < valFinalCol - 2) sb.Append(unicode ? "┴" : "+");
        else sb.Append(unicode ? "┘" : "+");
    }
    if (page != null) {
        sb.AppendLine();
        sb.Append(new string(' ', keyColWidth + 2));
        sb.AppendLine("< >");
    }
    yield return sb.ToString();
}

record TableData(
    int ValTotalColCount,
    int KeyColWidth,
    int? PageBounded,
    // 1-based
    int ValInitialCol,
    // 1-based
    int ValFinalCol,
    int ValColWidth,
    int TotalWidth,
    int TotalHeight
);

#endregion Table drawing

#region Classes

enum State {
    Main = 0,
    Sub = 1
}

class ParseException : Exception {
    public ParseException(string message) : base(message) {}
}

class CommandException : Exception {
    public CommandException(string message) : base(message) {}
}

record CartesianJoinScoreData(
    Symb A, 
    Symb B, 
    bool AHasB, 
    bool ReferenceAHasB,
    bool AHasBEqualsReferenceAHasB, 
    int Score
);

record Symb(string Class, string Name, int Id) : IComparable<Symb> {
    public override string ToString() => $"{Class}.{Name}";
    public int CompareTo(Symb other) => Name.CompareTo(other.Name);
}

class LoadFileStats {
    public int ClassesConstructed { get; set; }
    public int ClassElementsAdded { get; set; }
    public int RelationsDefined { get; set; }
    public int RelationElementsDefined { get; set; }
}

static class Symbols {
    static readonly Dictionary<string, HashSet<string>> classSymbols = new();
    
    static readonly Dictionary<string, int> classSeqPointers = new();
    
    public static Symb Create(
        string className, 
        string symbolName,
        TextWriter? dbg = null
    ) {
        if (classSymbols.TryGetValue(className, out HashSet<string>? hs) && 
            hs.Contains(symbolName))
            throw new ArgumentException(
                $"Class '{className}' already contains symbol '{
                    symbolName}'");
        if (hs == null)
            classSymbols.Add(className, hs = new());
        int id = !classSeqPointers.TryGetValue(className, out id) ?
            1 << 0 : id << 1;
        dbg?.WriteLine($"Symbols: {className}.{symbolName} is {id}");
        classSeqPointers[className] = id;
        hs.Add(symbolName);
        return new(className, symbolName, id);
    }
    
    public static void Clear() {
        classSymbols.Clear();
        classSeqPointers.Clear();
    }
}

#endregion Classes