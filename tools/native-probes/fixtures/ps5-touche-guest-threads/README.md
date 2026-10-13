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

`-SemaphoreGate` replaces the initial atomic gate with a zero-count semaphore,
then parks both workers again halfway through their work. Main releases two
tokens each time and verifies counter=1000 with neither worker finished at the
second gate. The runner requires four actual wait-block and four wait-wake trace
events in addition to final guest assertions. Both ordinary return and explicit
exit must preserve full-width results after those resumed continuations.

From the checkout in PowerShell:

```powershell
& tools/native-probes/Build-TouchePX5-GuestThreads.ps1
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1 -TrackingMode disabled
& tools/native-probes/Build-TouchePX5-GuestThreads.ps1 -ExitSecondWorker
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1
& tools/native-probes/Test-TouchePX5-GuestThreads.ps1 -TrackingMode disabled
```

Repeat both builds with `-SemaphoreGate` to exercise the resumed paths.

Each fresh process report contains ELF, executable, Core and HLE hashes. The
runner preserves the input ELF and build report beside logs before another
variant overwrites the builder output. PASS requires both host exit zero and
the notification emitted only after all guest assertions pass. It establishes
this two-worker create/join path, not arbitrary parallel schedules, cancellation,
mutex/condition correctness, thread-local cleanup, GPU producer ownership or
general PS5 compatibility. The write-watch selection alone does not exercise
tracked image writes in this fixture.

Semaphore-mode success additionally covers the bounded two-gate continuation
and wake path, not general semaphore scheduling, timeout/cancel/delete behavior.

The initial run exposed native returns being sign-extended from a 32-bit managed
call result. The runtime now captures native RAX in writable transition storage
before crossing that boundary. A validity flag distinguishes ordinary returns
from explicit thread exit, which already supplies its result through context
RAX. That distinction is covered by the explicit-exit variant.

The semaphore variant subsequently exposed ordinary resumed-thread returns
leaving the last import's RAX (zero) in the context. The continuation now patches
its original return slot to a local capture thunk before the shared host-return
stub. Explicit exits and further yields still use the common sentinel, preserving
their existing context result and block state rather than the local capture.
