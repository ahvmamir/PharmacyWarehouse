// The test host reads its database path from a process-wide environment variable (see TestApp),
// so tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]