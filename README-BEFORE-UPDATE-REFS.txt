Notes on updating the project references:


- System.Threading.Tasks.Extensions can NOT be updated to >4.5.4 or VS2019 extension wont work because of transitive dependency on Compilers.Services.Unsage which collides with System.Collections.Immutable.
