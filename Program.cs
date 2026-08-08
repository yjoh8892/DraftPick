using DraftPick.Components;
using DraftPick.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 드래프트 상태는 서버 메모리 한 곳에서만 관리한다.
builder.Services.AddSingleton<DraftRoomStore>();
builder.Services.AddSingleton<DiscordAnnouncer>();
builder.Services.AddHostedService<DraftTicker>();

// 디스코드가 응답하지 않아도 오래 매달리지 않도록 짧게 끊는다.
builder.Services.AddHttpClient(nameof(DiscordAnnouncer), http => http.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
