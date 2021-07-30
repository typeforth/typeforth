100 cells allocate throw constant &mem

\ store at &mem: docol: | . | EXIT
docol:      &mem           !
comp' .     &mem 1 cells + ! drop
comp' EXIT  &mem 2 cells + ! drop
42
&mem execute


bye
