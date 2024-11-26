Notes on updating the project references:

IMPORTANT VERSION CONSTRAINTS:
- Can't reference any library requiring System.Threading.Tasks.Extensions must not be updated beyond version 4.5.4
  Reason: The VS2019 extension will fail due to a transitive dependency conflict between
          Compilers.Services.Unsafe and System.Collections.Immutable.
- Can't update Polly beyond version 7.2.4 because of dependency conflicts with System.Threading.Tasks.Extensions.
- Can't update CommunityToolkit.Mvvm beyond v7.1.2 because of dependency conflicts with System.Threading.Tasks.Extensions in test framework.  
