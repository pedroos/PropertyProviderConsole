# Property Provider

Property Provider defines a set of operations on relations. This console program supports visualizing and manipulating such operations and relations from a small and portable executable with minimal dependencies, using a simple text file as project storage.

## Running

Run `pp.exe` or `pp` on your terminal.

## Commands

There are two levels of prompt: main (`PP>`) and table (`>`). Please inspect the prompt marker to determine the currently active prompt.

**Main commands**

`PP> exit`

Quit.

`PP> help`

Get help.

`PP> load [file path]`

Load an input file (full path, no quotes).

`PP> classes`

List loaded classes.

`PP> relations`

List loaded relations.

`PP> [class name]`

List the elements of a class.

`PP> [class name] v [class name]`

Plot a relation joining two classes.

`PP> [class name] v [class name] ! [element name]`

Score an element of a **key** class against elements in the same class with respect to a **value** class for **similarity**.

`PP> [class name] v [class name] !! [element name]`

Same as the last one, but shows **true** values (*characteristics*) instead of **equal** values.

`PP> [class name] v [class name] ? [element name]`

Score an element of a **key** class against elements in the same class with respect to a **value** class for **dissimilarity**.

`PP> [class name] v [class name] ?? [element name]`

Same as the last one, but shows **true** values (*characteristics*) instead of **equal** values.

`PP> outfile`

Prints the path of the output file.

**Table commands**

`> help`

Get help.

`> s|show`

Shows the current table.

`> b|break`

Breaks back into the main prompt as-is.

`> c|clear`

Breaks back into the main prompt, clearing the last table output.

> Obs.: this command is not working on Linux. We're working on a fix for it.

`> pp|pn|pf|pl|p[page number]`

Navigates to the previous, next, first, last, or specific page, respectively.

`> save`

Writes the complete last table output to the output file. Unlike visualization through the prompt, this output is not paged but rather contains the full table.

## Sample file

Please check the sample file `SampleFile.txt` for an example basic project.

## Input file format

The input text file has the following structure:

General rules

- Leading or trailing whitespace is ignored
- Empty lines are ignored
- Comment lines begin with `//` or `#`

1. Class declarations

   - Must be the first section
   - A comma delimits the class from the element
   - One declaration per line

   Example:

   ```
   Class1, Element1
   Class1, Element2
   ...
   
   Class2, Element1
   Class2, Element2
   ...
   ```

2. Relation declarations

   - Must be the second section
   - A comma delimits the key element from the value element
   - Elements are qualified in the format `[class name].[element name]`
   - A class can't be both a key and a value in a relation

   Example:

   ```
   Class1.Element1   , Class2.Element1
   Class1.Element1   , Class2.Element2
   
   Class1.Element2   , Class2.Element2
   ...
   ```

## Building

To build:

1. Download a `bflat v8.x` release from https://github.com/bflattened/bflat/releases and add the executable to your path
2. Adjust the paths and run the files or copy-and-paste the commands from `build.ps1` or `build.sh` into your terminal

### Dependencies

- C++ Standard Library

  - On Ubuntu 20.04, run:

    ```
    sudo apt update
    sudo apt install libc++-dev
    ```

    before building.