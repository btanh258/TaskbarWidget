# third party

`pawnio/IntelMSR.bin` and `pawnio/AMDFamily17.bin` are the signed modules from [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) 0.2.9 by namazso, LGPL-2.1 (see `pawnio/COPYING`). they get embedded in the exe as is.

release_0_2_9.zip sha256: `5eb25b128b0940c53c36418dc4f74fba66d5a80e6bf3d95eb0ae62f4d946222d`

IntelMSR.bin sha256: `5bfda87500160076158befc77300f81a388d72b11df2c794a3939ebe66777098`
AMDFamily17.bin sha256: `099dc01d6db97ea997fec4a461e191cc64b9d7ce47c9d2153c451c56c2adcf50`

the PawnIO driver itself isn't included. the app downloads PawnIO_setup.exe 2.2.0 from the official github release when you turn on cpu temp and only runs it if the sha256 matches `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`.
