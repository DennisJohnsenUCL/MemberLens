# MemberLens

**Intelligent IntelliSense completions for string-based member accessors in C#.**

MemberLens brings autocompletion to methods that accept member names as strings — eliminating typos, reducing guesswork, and keeping your code in sync with your types.

---
## Features

### Smart String Argument Completion
When you call a method whose parameter is decorated with `[MemberAccessor]`, MemberLens automatically surfaces the valid field or method names of the target type — right inside the standard IntelliSense dropdown.

### Generic Type Support
Works seamlessly with generic methods and generic classes. You can bind completions to a specific type argument index from either the method (`GenericSource.Method`) or the containing class (`GenericSource.Class`).

### Rich Hover Tooltips
Hovering over a completion item shows a detailed tooltip for the underlying symbol, so you always know exactly what you're selecting.

---
## How It Works

When you call a method whose string parameter is decorated with `[MemberAccessor]`, MemberLens reads the attribute to determine the target type and member kind, then presents matching member names as completion items in the IntelliSense dropdown — inserted as properly quoted strings.

Simply start typing in the method invocation and MemberLens will suggest any applicable field or method names, including private members.

To annotate method parameters with the MemberAccessor attribute yourself, get the [MemberLens.Attributes Nuget](https://www.nuget.org/packages/MemberLens.Attributes). See its description for how to annotate method parameters to bring MemberLens support to your code.

---
## Feedback & Contributions

Found a bug or have a feature request? Open an issue on the project repository.
