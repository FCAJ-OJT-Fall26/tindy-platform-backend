using Xunit;

// Database containers are expensive; avoid running test classes in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
