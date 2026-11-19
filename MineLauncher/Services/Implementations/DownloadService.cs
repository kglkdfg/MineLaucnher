using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MineLauncher.Services.Abstractions;
namespace MineLauncher.Services.Implementations;
public sealed class DownloadService : IDownloadService
{
 private readonly HttpClient _http;
 private const int MaxRetries = 3;
 public DownloadService(HttpClient http) => _http = http;
 public async Task DownloadFileAsync(string url,string destinationPath,IProgress<double>? progress=null,string? expectedSha1=null,long? expectedSize=null,CancellationToken ct=default)
 {
  ArgumentException.ThrowIfNullOrWhiteSpace(url); ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
  if(expectedSize is < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));
  if(expectedSha1 is not null && (expectedSha1.Length!=40 || !expectedSha1.All(Uri.IsHexDigit))) throw new ArgumentException("Invalid SHA-1.",nameof(expectedSha1));
  var full=Path.GetFullPath(destinationPath); var dir=Path.GetDirectoryName(full); if(!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
  var part=full+".part";
  if(File.Exists(full) && (expectedSize is null || new FileInfo(full).Length==expectedSize) && (expectedSha1 is null || await ShaMatches(full,expectedSha1,ct))) return;
  Delete(part);
  try {
   for(int attempt=1;;attempt++) {
    ct.ThrowIfCancellationRequested();
    try {
     using var response=await _http.GetAsync(new Uri(url),HttpCompletionOption.ResponseHeadersRead,ct);
     if(IsTransient(response.StatusCode) && attempt<MaxRetries) { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2,attempt-1)),ct); continue; }
     response.EnsureSuccessStatusCode();
     var length=response.Content.Headers.ContentLength;
     if(expectedSize.HasValue && length.HasValue && length.Value!=expectedSize.Value) throw new InvalidDataException("Content-Length mismatch.");
     await using var input=await response.Content.ReadAsStreamAsync(ct);
     await using var output=new FileStream(part,FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
     var buffer=new byte[81920]; long count=0; int read;
     while((read=await input.ReadAsync(buffer.AsMemory(),ct))>0) {
      await output.WriteAsync(buffer.AsMemory(0,read),ct); count+=read;
      var total=expectedSize ?? length; if(total is >0) progress?.Report(Math.Min(100,count*100d/total.Value));
     }
     await output.FlushAsync(ct);
     if(expectedSize.HasValue && count!=expectedSize.Value) throw new InvalidDataException("Downloaded file size mismatch.");
     if(expectedSha1 is not null && !await ShaMatches(part,expectedSha1,ct)) throw new InvalidDataException("Downloaded file SHA-1 mismatch.");
     File.Move(part,full,true); progress?.Report(100); return;
    } catch(Exception ex) when(attempt<MaxRetries && !ct.IsCancellationRequested && (ex is HttpRequestException || ex is IOException || ex is TimeoutException)) {
     Delete(part); await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2,attempt-1)),ct);
    }
   }
  } catch { Delete(part); throw; }
 }
 public async Task<T> GetJsonAsync<T>(string url,CancellationToken ct=default)
  => await _http.GetFromJsonAsync<T>(url,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),ct) ?? throw new InvalidOperationException($"Empty response: {url}");
 private static bool IsTransient(HttpStatusCode s) => s==HttpStatusCode.RequestTimeout || (int)s==429 || (int)s>=500;
 private static async Task<bool> ShaMatches(string p,string expected,CancellationToken ct) { await using var f=File.OpenRead(p); return Convert.ToHexString(await SHA1.HashDataAsync(f,ct)).Equals(expected,StringComparison.OrdinalIgnoreCase); }
 private static void Delete(string p) { try { if(File.Exists(p)) File.Delete(p); } catch(IOException){} }
}