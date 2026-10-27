Notes on updating the project references:

IMPORTANT VERSION CONSTRAINTS:
- Can't reference any library requiring System.Threading.Tasks.Extensions must not be updated beyond version 4.5.4
  Reason: The VS2019 extension will fail due to a transitive dependency conflict between
          Compilers.Services.Unsafe and System.Collections.Immutable.
- Can't reference any library requiring Microsoft.Bcl.AsyncInterfaces beyond version 5.0 (on .NET Framework).
  Reason: VS2019 binding redirects for it stop at 5.0.0.0 (e.g. Polly 8 requires 6.0, through Polly.Core).
  Polly is no longer used: retries are done with the internal RetryHelper.
