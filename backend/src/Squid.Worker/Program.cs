// Composition root of the worker process: media, backup, cipher and Reed-Solomon (doc 05 §4.6).
// It stays a separate process exactly so that heavy CPU never lands on the silo scheduler.
// Nothing is hosted yet: the first real worker brings its own hosted service and shutdown
// contract. Printing and exiting keeps the composition root honest until then.
Console.WriteLine("Squid.Worker: no work item implemented yet (arrives with the media and backup slices).");