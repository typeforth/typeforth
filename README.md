# Typed Forth
> **WARN:** This has not yet been implemented, although progress is underway.
> The first implementation will be [triforth](https://github.com/civboot/triforth)

It should be possible to add types and typechecking to
[forth](https://en.wikipedia.org/wiki/Forth_(programming_language)) with zero
runtime cost and _very little_ compiler and memory cost. This will NOT be ANS
compliant, as we need `.` for module/method/interface access (yes, we have
those) and so we might as well cleanup a few other things in the meantime.

What is this magic? It all works in my head, lets see if it works on paper.

Typeforth is _a forth_. It's core can be implemented in ~1000 lines of assembly
(more with comments+tests), from which it bootstraps itself. It defines words
using only characters separated by whitespace. It has immediate words, etc etc.
For details on the internals of forth, check out [Starting
Forth](https://www.forth.com/starting-forth/) or a tutorial of your choice.

The fundamentals of typechecking work by storing a 16bit wordId for every word,
stored in it's flags cell. The wordId is a lookup into a global "vector"
(stored in an extremely minimal 1k-block arena allocator), which contains some
metadata including an array of the 16bit input and output types. When a word is
being compiled, the input types for the word are put on a temporary typestack
and as each word is compiled, the typestack keeps track by poping/pushing the
inputs/outputs of the word that's being compiled. At the end of the
compilation, it checks to make sure the typestack is equal to the types
specified in the output. Branching works by making a checkpoint of the
typestack and exploring each branch, ensuring they are all equal.

Types don't take up space in the dictionary (besides the 16bit wordId in each
word), they exist only within the typearena, which can be serialized to a file
and temporarily dropped when "compilation" is done and a large program needs to
be run. It can then be deserialized to enable further compilation.

Datatypes like structs (aka prodtypes), enums (aka tagged unions/sumtypes),
modules and interfaces are only stored within the typearena (except a vtable
for interface implementations). Even though these can be "thrown away" before
execution, they are still extremely compact.

Typechecking only happens for runtime words, so IMMEDIATE words don't have to
be typechecked if they don't compile code. If they _do_ compile code, it is
their job to push/pop from the typestack or call appropriate comipilation
functions to do it for them. In this way, you still have all of the control
as in normal forth, but now it is easier to create typesafe interfaces to
catch the most egregious mistakes. Additionally, typechecking can be bypassed
via a simple `nocheck`, enabling writing performant algorithms.

When declaring a word you can specify the type inside of `T{ ... }`, which
can also specify local variables. This stanza is very similar to the ANS
type structure `{ ... }` except it doesn't have to have same performance
pentalties of moving items to local values.

Types are specified in a few forms:
- `: myFun { flag u:index &MyArray:arr | u:i -- d }` this is a fairly
  standard usage. This function accepts an unsigned cell (index), a MyArray
  _pointer_ (&) and a flag. It also has a local unsigned int named `i`.  Only
  the first two inputs and the local variable are named, they can be accessed
  by name within the function. The flag is left on the stack but is still
  part of the word signature. Note: "&" prefix specifies a reference.
- `: myVFun { u:index &v{ Vec Debug }:v -- d }` this accepts the vref
  (virtual reference) `v` which must contain the `Vec` and `Debug`
  interfaces in it's vtable.
- `: tswap { generic }`: the input and output are generic. The documentation
  for tswap is `( A B -- B A )`. It achieves this by programatically
  inspecting the typestack for the size of the top two types and writes this
  code into the function being compiled. It then alters the typestack
  appropriately as well. In typeforth, generics are a Simple Matter of
  Programming the necessary type manipulation to match the data maniuplation.
  With it you can create generic map/filter/etc constructs, generic datatypes
  and many more things.
- `: mapu { &u:addr u:count '{ u -- u }:f }` this accepts the xt `f` which will
  presumably be executed over `count` values in `addr`
- `: main { clear -- }`: `clear` means the function expects an empty stack.
- `: clear { * -- }`: `*` is reserved for consuming the ENTIRE stack. Words of
  this type can only be used at the highest level or in functions that expect
  an empty stack (i.e. `{ clear -- ... }` or other functions that clear the
  stack.
- `: myOptimizedFunction { nocheck: u -- u }`: the function is not
  type-checked, allowing for using any arbitrary code including raw assembly.
  You cannot use local variables in nocheck functions.


## Some Notes on Implementation
When a word is being compiled it encounters the `{ ... }` signature. It encodes
the type indexes found into the typestack and creates a checkpoint.

While compiling a word, it keeps track of the type-stack by comparing and
popping the xts of the inputs and pushing the xts of the outputs. It ensures
that the values are kept consistent throughout.

When an xt is "compiled" into a word the compile-time code for that xt will
also pop/push the _types_ onto the typestack -- throwing an error if things
don't match.

- IF+ELSE+THEN or SWITCH statements are regarded as "block" and are handled thusly:
  - BLOCKSTART marks the BLOCKSTART on the typestack followed by a snapshot (copy)
    of the current type-stack into a new memory location.
  - It then compiles the types as normal, using the new typestack
    - Note: If it encounters another BLOCKSTART then that block is checked
      first (by creating a new snapshot of the current stack) and it's
      input/output is used directly.
  - When it comes accross a BLOCKALT or BLOCKEND (i.e. ELSE, CASE, THEN, etc)
    it walks it's own stack backwards (the "return" stack of the block)
    comparing with the previous typestack. From this it infers the input/output
    types and encodes them.
      - This is rather simple actually. It starts at the bottom of both stacks
        and compares types until they don't match. These are discarded. The
        remaining types on the previous-stack are "input" types for the block
        and the remaining types on the block-stack are "output" types.
  - BLOCKALT do the _exact same procedure_. They clone the input stack etc. The
    difference is that when their types are inferred they compare them to the
    BLOCKSTART type signature.
- Most loops are not allowed to change the type-stack. BEGIN...AGAIN statements
  can return a value only within an internal block directly before a BREAK.
  - TODO: not sure how this is implemented, probably similar to above.

**Local values:** Local values are kept on the return stack. Unlike some forths,
which have to deal with cleaning up return stacks by clever compilation of
branch instructions, this forth has an assembly-implemented xt for:
  - Increasing the Rstack by the localsSize (8bit value) of the next xt (in it's flags)
  - Executing the xt, as normal. This means the return address is ON TOP of
    the locals, so EXIT works normally.
  - Decreasing the Rstack by the localsSize in the same way.

Alternatively, the typechecker can track the R stack's types using a separate
typestack. Note that these can't be used together and also that neither are
required: type checking does not require moving values into "locals", the
values can simply remain on the stack.

**Structs:** structs are have a word-type of struct. They have no runtime
behavior, and at compiletime simply modify the type-stack to accept their
inputs and output themselves. They store their input types as their fields --
their output type is their own wordId.  They also store the interface wordIds and
construct a vtable (described more below), which does actually get stored in
the dictionary.

```
STRUCT
  field type1 name1
  field type1 name2
  implements Interface1
  implements Interface2
END
```

**Methods:** a concrete method can be added to any struct by simply defining a
:METHOD which accepts a ref to the struct as it's last input, conventionally
named `self` (or unnamed). The method can then be called with `. myMethod`
(yes, we replace . -- use .u instead) `.` will lookup the method named
"myMethod" using the last value on the type-stack, which it will typecheck at
compile time and execute at runtime.

**Enums:** enumerations are rust-like and can contain both a variant and a
value. They are very similar to structs but have a few default methods and a
variantTable which is equal to inpSize&outSize.
- variantTable: contains numVariants cells after enumInfo, each cell contains
  the xt of method associated with that vi (variant index).
- `:METHOD vi { &MyEnum:self -- u }`: method to return the vi (variant index) which is
  an index into the variantTable.

Each variant is then encoded with it's own method used in ECASE. For example:
- `:method u { &MyEnum -- u } self @ ;`
- `:method d { &MyEnum -- d } 1 d@i ;` double at index 1 of address
- `:method empty { &MyEnum:self -- } ;`


Example use below. SWITCHE conceputally calls `dup $m vi SWITCH`. Each ECASE
will DROP the appropraite number of cells for the size of that variant relative
to the enum (the enum always takes up the same size on the stack).
```
myEnumValue SWITCHE
  ECASE u   # 1 cell dropped
    ... do stuff with u
  ENDCASE
  ECASE d   # no cells dropped
     ... do stuff with d
  ENDCASE
  ECASE empty # 2 cells dropped
    .... do stuff with nothing
  ENDCASE
THEN
```

Example definition with `( inline comments )`
```
ENUM
  VARIANT empty ( =name) EMPTY ( =type)
  VARIANT u ( =name)     u ( =type)
  VARIANT d ( =name)     d ( =type)
  IMPLEMENTS Debug
END
```

**VTable**: Space is reserved for the VTable when the Struct/Enum is
constructed and a perfect hashing function is dynamically created for the
struct to convert the xt of a virtual method into the appropriate method to
call. The xt of the perfect hashing function is stored in the first index
of the vTable.

To then assign methods to the type in question, use

```
IMPLEMENT Debug MyType
  dbg ( =Debug method) myDbg ( =your implementation)
END
```

**Modules** are implemented with a `MODULE myModuleName` which creates a dictionary
entry of type "module" and assigns it a new moduleId. Words defined after this
(including from other modules) will be of the associated module. If the module
is already defined, it will tell the interpreter to not execute the import.

`IMPORT path/to/myModule.fs` will import a module. the MODULE line will prevent
duplicate imports.

A word in a module can be looked up via `myModule . someWord`. FIND will only
find words with myModules's moduleId

`USE module1` will put the module in a global array so that FIND will find them
without a module-lookup. `USE clear` will clear this array, leaving only the
CORE module.

It should be noted that the following characters are not allowed in the name for
struct/enum/interface: `& ^ * @`

## License
The name "TypeForth" is licensed as 
[CC BY-ND](https://creativecommons.org/licenses/by-nd/4.0/). It is the intention
of the author to create a standard based on this name, and therefore wishes for
the name to not be polluted by other standards. Eventually, this name should
be Trademarked.

All other bits are dual-licensed under [MIT](./LICENSE) OR the
[UNLICENSE](./UNLICENSE), at your discression.

It is the intent that this project is in the public domain to be modified in
any way with no warranties of any kind.

