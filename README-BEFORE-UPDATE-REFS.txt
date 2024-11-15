Notes on updating the project references:

IMPORTANT VERSION CONSTRAINTS:
- System.Threading.Tasks.Extensions must not be updated beyond version 4.5.4
  Reason: The VS2019 extension will fail due to a transitive dependency conflict between
          Compilers.Services.Unsafe and System.Collections.Immutable.
