# Native guest thread ABI probe

Original GPL-2.0-or-later source with an unchanged, pinned ps5link-sdk startup.
The builder emits an unsigned native SCE ELF using the existing local SDK linker.
It calls the actual native import bridge, scheduler and join implementation.

Two guest-created workers wait for an atomic gate, perform 1000 increments each
of one shared atomic counter, maintain separate totals, and finish. Main joins
both and checks their distinct full-width results, `12345678CAFE0001` and
`12345678CAFE0002`. The optional second-worker exit variant calls
`scePthreadExit` instead of returning; code following that call must not run.
Guest-side progress deadlines are five seconds; the runner caps its owned host
process at 20 seconds. No graphics, input or commercial game assets are involved.

From the checkout in PowerShell:

```powershell
& tools/native-probes/Build-TouchePX5-GuestThreads.ps1
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1 -TrackingMode disabled
& tools/native-probes/Build-TouchePX5-GuestThreads.ps1 -ExitSecondWorker
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1 -TrackingMode disabled
```

Each fresh process report contains ELF, executable, Core and HLE hashes. The
runner preserves the input ELF and build report beside logs before another
variant overwrites the builder output. PASS requires both host exit zero and
the notification emitted only after all guest assertions pass. It establishes
this two-worker create/join path, not arbitrary parallel schedules, cancellation,
mutex/condition correctness, thread-local cleanup, GPU producer ownership or
general PS5 compatibility. The write-watch selection alone does not exercise
tracked image writes in this fixture.

The initial run exposed native returns being sign-extended from a 32-bit managed
call result. The runtime now captures native RAX in writable transition storage
before crossing that boundary. A validity flag distinguishes ordinary returns
from explicit thread exit, which already supplies its result through context
RAX. That distinction is covered by the explicit-exit variant.
