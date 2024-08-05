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

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static System.Console;
using static System.Environment;
using static System.Text.Encoding;
using static Utils;
using static State;

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
        if (ln.Length == 0 || ln.StartsWith("//") || ln.StartsWith("#")) 
            continue;
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
        if (!aIsQualified) {
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
                    throw new ParseException($"Line {i + 1}, term {tno
                        }: undeclared class '{clss}'");
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

var dbg = args.LongArgExists("dbg") ? Out : null;

// Shield the values from modification

static Constants GetConstants(string[] args) {
    string outDir = GetFolderPath(SpecialFolder.LocalApplicationData)
        .Combine("PropertyProvider");
    if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
    string outPath = outDir.Combine("PropertyProviderOutput.txt");
    
    return new(outPath);
}

Lazy<Constants> constants = new(() => GetConstants(args));

#endregion

#region State

State state = Main;
int currPage = 1;
(string, string) currRelation = default!;
Symb? currReferenceA = null;
bool currDissimilarity = false;
bool currDisplayEquality = false;
string currPrompt = null!;
string currTableInput = null!;
string? autoInput = null;

CancellationTokenSource mainCts = new();
CancellationTokenSource cmdCts = null;

#endregion State

#region Event handlers

// Cancel/SIGINT (Ctrl + C) handler

CancelKeyPress += (_, e) => {
    dbg?.WriteLine("CancelKeyPress");
    e.Cancel = true;
    #if LINUX
    cmdCts.Cancel();
    #endif
};

// Exit handler

AppDomain.CurrentDomain.ProcessExit += (_, _) => {
    dbg?.WriteLine("ProcessExit");
    mainCts.Cancel();
    mainCts.Dispose();
    cmdCts?.Dispose();
    WriteLine();
};

#endregion Event handlers

#region Loop

WriteLine("PropertyProvider v0.1.1");
WriteLine();

if (args.LongArgExists("help") || args.ShortArgExists('h')) {
    WriteLine("""
    Syntax: pp [--file [input file name] (optional)]
    """);
    return 0;
}
if (
    args.TryGetArgv("file", out string infl) ||
    args.TryGetArgv("f", out infl, twoDashes: false)
) autoInput = $"load {infl}";

void WritePrompt() {
    currPrompt = state switch {
        Main => "PP> ",
        Sub => "> "
    };
    Write(currPrompt);
}

/*
e.Cancel interrupts ReadOnline() on Windows, not on Linux. Thus, control flow 
resumes on Windows while ReadLine runs in the background and a loop task running 
in the foreground prints new prompts upon being cancelled (using Wait(token)) on 
Linux (interrupting ReadLine on Linux interferes with subsequent input).
*/

#if LINUX

// Input loop (background)

Task.Run(() => {
    while (!mainCts.IsCancellationRequested) {
        WritePrompt();
        string inpt = autoInput ?? ReadLine();
        if (inpt == null) throw new UnreachableException();
        else if (inpt == "exit") {
            mainCts.Cancel();
            cmdCts?.Cancel();
        }
        else HandleInput(inpt);
    }
});

// Cancel loop (foreground)

while (!mainCts.IsCancellationRequested) {
    cmdCts?.Dispose();
    cmdCts = new();
    
    dbg?.WriteLine("Start cancel loop");
    try {        
        while (true) 
            Task.Delay(1000, cmdCts.Token).Wait(cmdCts.Token);
    }
    catch (OperationCanceledException) {
        dbg?.WriteLine("Command cancellation requested");
        switch (state) {
        case Sub:
            // Equivalent to b|break
            state = Main;
            WriteLine(); WriteLine();
            WritePrompt();
            break;
        case Main:
            // Exit
            mainCts.Cancel();
            break;
        }
    }
}

cmdCts.Cancel();

#else

while (!mainCts.IsCancellationRequested) {
    WritePrompt();
    string inpt = autoInput ?? ReadLine();
    if (inpt == null) {
        switch (state) {
        case Sub:
            // Equivalent to b|break
            state = Main;
            WriteLine(); WriteLine();
            break;
        case Main:
            // Exit
            mainCts.Cancel();
            break;
        }
    }
    else if (inpt == "exit") {
        mainCts.Cancel();
        cmdCts?.Cancel();
    }
    else HandleInput(inpt);
}

#endif

return 0;

void HandleInput(string inpt) {
    if (autoInput != null) { WriteLine(); autoInput = null; }
    int inptLen = inpt.Length + currPrompt.Length;
    try {
        switch (state) {
        case Main:
            ProcessMainCmd(inpt);
            break;
        case Sub:
            ProcessSubCmd(inpt);
            break;
        }
    }
    catch (CommandException ex) {
        WriteLine(ex.Message);
    }
    WriteLine();
}

void ProcessMainCmd(string inpt) {
    if (inpt.In("h", "help")) {
        string ln = """
        exit, outfile, classes, relations, [className]
        load [filename]
        [className] v [className]
        [className] v [className] (.|..|,|,,) [classElement]
        """;
        WriteLine(ln);
    }
    else if (inpt == "outfile") {
        WriteLine(constants.Value.OutPath);
    }
    else if (inpt.StartsWith("load ")) {
        string pth = inpt[5..];
        
        if (!Path.IsPathFullyQualified(pth)) 
            // Try to qualify the path using the current directory
            pth = CurrentDirectory.Combine(pth);
        
        if (!File.Exists(pth)) 
            throw new CommandException($"The file '{pth}' was not found");
        try {
            LoadFile(pth, out LoadFileStats stats, dbg: dbg);
            
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
        WriteLine(classes.Keys.CheckEmpty(x => x.Join(", "), 
            "No loaded classes"));
    }
    else if (inpt == "relations") {
        WriteLine(relations.Keys.CheckEmpty(x => x.Join(", "),
            "No loaded relations"));
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
            throw new CommandException($"Relation not found: {key}, {val}");
        currRelation = (key, val);
        currReferenceA = null;
        state = Sub;
        currPage = 1;
        currTableInput = inpt;
        autoInput = "display";
    }
    else if (inpt.Match("^([a-zA-Z]+) (#|x|v|vs|vs.) ([a-zA-Z]+) " + 
        "(\\.|\\.\\.|,|,,) ([a-zA-Z]+)$", out match)) 
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
            throw new CommandException($"Relation not found: {key}, {val}");
        if (!keyClass.SingleGet(x => x.Name == rfa, out currReferenceA)) 
            throw new CommandException($"Element not found: {key}.{rfa}");
        currRelation = (key, val);
        currDissimilarity = eq.In(",", ",,");
        currDisplayEquality = eq.In(".", ",");
        state = Sub;
        currPage = 1;
        currTableInput = inpt;
        autoInput = "display";
    }
    else throw new CommandException("Unrecognized command");
}

void ProcessSubCmd(string inpt) {
    bool save = false;
    
    if (inpt.In("h", "help")) {
        string ln = "b|break, pp, pn, pf, pl, p[pageNumber]";
        WriteLine(ln);
        return;
    }
    else if (inpt.In("b", "break")) {
        state = Main;
        return;
    }
    else if (inpt == "save") {
        save = true;
    }
    else if (inpt.In("d", "display")) {
        int numPages = classes[currRelation.Item2].Count;
        currPage = 1;
    }
    else if (inpt.Match("^p([0-9]+|p|n|f|l)$", out Match match)) {
        int numPages = classes[currRelation.Item2].Count;
        
        string parm = match.Groups[1].Value;
        switch (parm) {
        case "p":
            if (currPage > 1) currPage--;
            break;
        case "n":
            if (currPage < numPages) currPage++;
            break;
        case "f":
            currPage = 1;
            break;
        case "l":
            currPage = numPages;
            break;
        default:
            int pg = int.Parse(parm);
            currPage = 
                (pg < 1) ? 1 :
                (pg > numPages) ? numPages :
                pg;
            break;
        }
    }
    else {
        WriteLine("Unrecognized command");
        return;
    }
    
    if (!relations.TryGetValue(currRelation, out List<(Symb, Symb)> rel)) 
        throw new CommandException($"Relation not found: {currRelation}");
    if (!classes.TryGetValue(currRelation.Item1, out List<Symb> keyClass)) 
        throw new CommandException($"Class not found: {currRelation.Item1}");
    if (!classes.TryGetValue(currRelation.Item2, out List<Symb> valClass)) 
        throw new CommandException($"Class not found: {currRelation.Item2}");
    
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
        
        var lns = DrawTableBoolean(
            table: t,
            keyColumnName: currRelation.ToString(),
            columnsName: "",
            keyToString: k => k.Name,
            valueToString: v => v.Name,
            tableData: tableData,
            page: !save ? currPage : null,
            unicode: false,
            dbg: dbg
        );
        
        if (!save) WriteLines(lns);
        else File.AppendAllLines(constants.Value.OutPath, 
            lns.Prepend(currTableInput + NewLine).Append(NewLine));
    }
    else {
        var t = ScoreGrouped(CartesianJoinScore(
            keyClass, 
            valClass, 
            rel, 
            currReferenceA,
            currDissimilarity
        ), displayEquality: currDisplayEquality);
        
        string keyColName = currRelation.ToString();
        
        var tableData = GetTableData(
            table: t,
            keyColumnName: keyColName,
            columnsName: "",
            keyToString: k => k.A.Name,
            valueToString: v => v.Name,
            page: !save ? currPage : null
        );
        
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
        
        if (!save) WriteLines(lns);
        else File.AppendAllLines(constants.Value.OutPath, 
            lns.Prepend(currTableInput + NewLine).Append(NewLine));
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
    bool displayEquality = false
) =>
    from g in data.GroupBy(x => x.A)
    let scr = g.Sum(x => x.Score)
    orderby scr descending
    select new Grouping<(Symb A, int Score), (Symb B, bool AHasB)>(
        (g.Key, scr), 
        g.Select(x => (
            x.B, 
            (!displayEquality ? x.AHasB : x.AHasBEqualsReferenceAHasB)
                .NegateIf(dissimilarity)
        )));

#endregion Transformations

#region Table drawing

static TableData GetTableData<TK, TV>(
    IEnumerable<IGrouping<TK, (TV, bool)>> table,
    string keyColumnName,
    string columnsName,
    Func<TK, string> keyToString,
    Func<TV, string> valueToString,
    int? page = null,
    TextWriter? dbg = null
) where TK : notnull where TV : notnull {
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

static IEnumerable<string> DrawTableBoolean<TK, TV>(
    IEnumerable<IGrouping<TK, (TV, bool)>> table,
    string keyColumnName,
    string columnsName,
    Func<TK, string> keyToString,
    Func<TV, string> valueToString,
    // If not passed, it will be calculated
    TableData? tableData = null,
    int? page = null,
    bool unicode = false,
    TextWriter? dbg = null
) where TK : notnull where TV : notnull {
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
    TV[] colCheck = null!;
    
    foreach (var kv in table) {
        sb.Clear();
        sb.Append(unicode ? "│ " : "| ");
        sb.Append((keyToString(kv.Key) ?? "").PadRight(keyColWidth, ' '));
        sb.Append(unicode ? " │ " : " | ");
        // Columns
        var cols = kv.Order().ToArray();
        var colNames = cols.Select(x => x.Item1).ToArray();
        if (colCheck != null && !colNames.SequenceEqual(colCheck)) 
            throw new ArgumentException($"""
                Inconsistent columns: expected
                {colCheck.Strs().Quoted().Join(", ")} but got
                {cols.Strs().Quoted().Join(",  ")}
             """);
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
        // `WriteLines` adds a trailing newline
        sb.Append("< >");
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
    Main,
    Sub
}

class ParseException : Exception {
    public ParseException(string message) : base(message) {}
}

class CommandException : Exception {
    public CommandException(string message) : base(message) {}
}

record Constants(
    string OutPath
);

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
                $"Class '{className}' already contains symbol '{symbolName}'");
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
