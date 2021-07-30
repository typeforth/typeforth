hex \ All numbers will be in hex

\ Set up RTime (runtime), CTime (compile time) and LTime (local time)
\ dictionaries

\ Reserve 0x400 buckets for dictionary entries.
400 constant numBuckets
numBuckets cells allocate throw constant &dictBuckets

: allocateWithHere IMMEDIATE \ ( bytes -- )
  \ Call like <bytes> allocateWithHere <size> <&min> <&here>
  dup constant \ consumes <size> word
  ( bytes) allocate throw dup ( &min &min)
  constant \ consumes <&min> word
  constant \ consumes &here word
;
10000 allocateWithHere dictRTimeMetaSize  &dictRTime0  &dictRTimeHere
10000 allocateWithHere cTimeCodeSize      &cTime0      &cTimeHere
10000 allocateWithHere dictCTimeMetaSize  &dictCTime0  &dictCTimeHere
1000  allocateWithHere lTimeSize          &lTime0      &lTimeHere

variable &wordTime \ bitmask of localtime | (runtime/compiletime)
01 constant WORD_TIME_RTIME
03 constant WORD_TIME_LTIME


.\" hello world!\n"
\ bye
