# Windows native red-zone fault probe

Run from the repository root with the installed .NET 10 SDK:

```powershell
C:\Dev\dotnet10\dotnet.exe run --project tools/native-probes/red-zone-fault/RedZoneFault.csproj -c Release -- artifacts/external-homebrew/ps5link-cube/windows-red-zone-native-veh-final.json
```

Windows x64 only. The process owns three VirtualAlloc pages and a vectored
exception handler; it removes that handler and frees its pages on normal exit.
No emulator libraries, graphics, downloads, or persistent settings are involved.

The emitted native leaf writes 16 distinct qwords below RSP, performs one
potentially protected-page write, and reads the markers back. A separate native
handler verifies the exact write access violation/address, snapshots all 16
markers **before its first call or push**, unprotects the page, and retries.
The second mode places the markers inside an allocated stack frame. The third
retains red-zone markers and biases RSP by -128 only around the memory store,
restoring it afterwards with flag-preserving LEA instructions.

Six cases run: each mode with and without the fault. Success requires zero
corruption in all controls, one handled exception in each fault case, the store
completed, and red-zone corruption already present at native handler entry.
Thus exit 0 means the incompatibility was **reproduced**, not fixed. On an OS
that doesn't reproduce it this diagnostic deliberately fails. JSON includes
OS/runtime, assembly/native-handler hashes and changed slots. The native hash
includes relocated addresses and therefore changes across processes.

Two fresh-process runs on Windows 10.0.19045 / .NET 10.0.3 reproduced two damaged
qwords each; the offsets varied (-64/-72, then -16/-24). Don't hard-code the
offsets or assume all 128 bytes remain safe except those two slots.

This matches Microsoft's [x64 stack usage contract](https://learn.microsoft.com/en-us/cpp/build/stack-usage):
storage below current RSP is volatile on Windows. It demonstrates that fixing
only SharpEmu's managed handler or moving its helper calls cannot recover bytes
already overwritten before handler entry. It does **not** identify the exact
Windows dispatcher instruction, validate all exceptions/threads, or implement
general guest instruction instrumentation.

The guarded-store result only covers a single register-addressed 32-bit store.
An emulator implementation would need decoded operand adjustment for RSP-based
instructions, preserved registers/flags, correct fault-context translation,
and coverage of every relevant fault site. Other exceptions and asynchronous
interruptions remain outside this probe. Non-fault dirty tracking is a separate
candidate, requiring allocation and CPU/GPU ownership investigation first.
