using Newtonsoft.Json;
using HttpServer.Models;
using Common.Database;
using Newtonsoft.Json.Linq;

namespace HttpServer.Controllers
{
    public class AccountController
    {
        public static void AddHandlers(WebApplication app)
        {
            app.Map("/account/risky/api/check", (HttpContext ctx) =>
            {
                RiskyCheck rsp = new()
                {
                    Retcode = 0,
                    Message = "",
                    Data = new RiskyCheck.DataScheme()
                    {
                        Id = "",
                        Action = "ACTION_NONE",
                        Geetest = null
                    }
                };

                ctx.Response.Headers.Add("Content-Type", "application/json");

                return ctx.Response.WriteAsync(JsonConvert.SerializeObject(rsp));
            });

            // TODO: Check username from request, create acc if needed and return it instead of hardcoded account?

            app.MapPost("/account/ma-cn-passport/app/loginByPassword", (HttpContext ctx) => ctx.Response.WriteAsJsonAsync(new
            {
                retcode = 0,
                message = "OK",
                data = new
                {
                    token = new
                    {
                        token_type = 1,
                        token = "8afe0a07-281d-4da8-b19b-2fdfb7842691"
                    },
                    user_info = new
                    {
                        aid = "1001",
                        mid = "",
                        account_name = "admin",
                        email = "",
                        is_email_verify = 0,
                        area_code = "**",
                        mobile = "",
                        safe_area_code = "",
                        safe_mobile = "",
                        realname = "",
                        identity_code = "",
                        rebind_area_code = "",
                        rebind_mobile = "",
                        rebind_mobile_time = "315532800",
                        links = new object[0],
                        country = "SG",
                        password_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                        is_adult = 0,
                        unmasked_email = "",
                        unmasked_email_type = 0
                    },
                    ext_user_info = new
                    {
                        guardian_email = "",
                        birth = "0"
                    },
                    reactivate_action_ticket = "",
                    bind_email_action_ticket = ""
                }
            }, default(CancellationToken)));
            app.MapPost("/{game_biz}/account/ma-passport/api/appLoginByPassword", (HttpContext ctx) => ctx.Response.WriteAsJsonAsync(new
            {
                retcode = 0,
                message = "OK",
                data = new
                {
                    token = new
                    {
                        token_type = 1,
                        token = "8afe0a07-281d-4da8-b19b-2fdfb7842691"
                    },
                    user_info = new
                    {
                        aid = "1001",
                        mid = "",
                        account_name = "admin",
                        email = "",
                        is_email_verify = 0,
                        area_code = "**",
                        mobile = "",
                        safe_area_code = "",
                        safe_mobile = "",
                        realname = "",
                        identity_code = "",
                        rebind_area_code = "",
                        rebind_mobile = "",
                        rebind_mobile_time = "315532800",
                        links = new object[0],
                        country = "SG",
                        password_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                        is_adult = 0,
                        unmasked_email = "",
                        unmasked_email_type = 0
                    },
                    ext_user_info = new
                    {
                        guardian_email = "",
                        birth = "0"
                    },
                    reactivate_action_ticket = "",
                    bind_email_action_ticket = ""
                }
            }, default(CancellationToken)));
            app.MapPost("/{game_biz}/account/ma-passport/token/getByGameToken", (HttpContext ctx) => ctx.Response.WriteAsJsonAsync(new
            {
                retcode = 0,
                message = "OK",
                data = new
                {
                    token = new
                    {
                        token_type = 1,
                        token = "8afe0a07-281d-4da8-b19b-2fdfb7842691"
                    },
                    user_info = new
                    {
                        aid = "1001",
                        mid = "",
                        account_name = "admin",
                        email = "",
                        is_email_verify = 0,
                        area_code = "**",
                        mobile = "",
                        safe_area_code = "",
                        safe_mobile = "",
                        realname = "",
                        identity_code = "",
                        rebind_area_code = "",
                        rebind_mobile = "",
                        rebind_mobile_time = "315532800",
                        links = new object[0],
                        country = "SG",
                        password_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                        is_adult = 0,
                        unmasked_email = "",
                        unmasked_email_type = 0
                    }
                }
            }, default(CancellationToken)));

#pragma warning disable CS8600, CS8602 // Converting null literal or possible null value to non-nullable type.
            app.MapPost("/{game_biz}/combo/granter/login/v2/login", (ctx) =>
            {
                StreamReader Reader = new(ctx.Request.Body);
                GranterLoginBody Data = JsonConvert.DeserializeObject<GranterLoginBody>(Reader.ReadToEndAsync().Result);
                GranterLoginBody.GranterLoginBodyData GranterLoginData = JsonConvert.DeserializeObject<GranterLoginBody.GranterLoginBodyData>(Data.Data);
                

                return ctx.Response.WriteAsJsonAsync(new
                {
                    retcode = 0,
                    message = "OK",
                    data = new {
                        combo_id = "0",
                        open_id = GranterLoginData.Uid,
                        combo_token = GranterLoginData.Token,
                        data = JsonConvert.SerializeObject(new
                        {
                            guest = GranterLoginData.Guest
                        }),
                        heartbeat = false,
                        account_type = 1,
                    }
                });
            });
            
            app.MapPost("/{game_biz}/mdk/shield/api/verify", (ctx) =>
            {
                StreamReader Reader = new(ctx.Request.Body);
                ShieldVerifyBody Data = JsonConvert.DeserializeObject<ShieldVerifyBody>(Reader.ReadToEndAsync().Result);
                UserScheme? user = User.FromToken(Data.Token);

                ShieldLoginResponse rsp = new()
                {
                    Retcode = 0,
                    Message = "OK",
                    Data = new()
                    {
                        Account = null
                    }
                };

                if (user != null)
                {
                    rsp.Data = new()
                    {
                        Account = new()
                        {
                            Uid = user.Uid,
                            Name = user.Name,
                            Email = "",
                            Mobile = "",
                            IsEmailVerify = "0",
                            Realname = "",
                            IdentityCard = "",
                            Token = user.Token.ToString(),
                            SafeMobile = "",
                            FacebookName = "",
                            GoogleName = "",
                            TwitterName = "",
                            GameCenterName = "",
                            AppleName = "",
                            SonyName = "",
                            TapName = "",
                            Country = "SG",
                            ReactivateTicket = "",
                            AreaCode = "**",
                            DeviceGrantTicket = "",
                            SteamName = "",
                            UnmaskedEmail = "",
                            UnmaskedEmailType = 0
                        },
                        DeviceGrantRequired = false,
                        SafeMoblieRequired = false,
                        RealpersonRequired = false,
                        ReactivateRequired = false,
                        RealnameOperation = "None"
                    };
                }

                ctx.Response.Headers.Add("Content-Type", "application/json");

                return ctx.Response.WriteAsync(JsonConvert.SerializeObject(rsp));
            });

            app.MapPost("/{game_biz}/mdk/shield/api/login", (ctx) =>
            {
                StreamReader Reader = new(ctx.Request.Body);
                ShieldLoginBody Data = JsonConvert.DeserializeObject<ShieldLoginBody>(Reader.ReadToEndAsync().Result);

                UserScheme user = User.FromName(Data.Account);

                ShieldLoginResponse rsp = new()
                {
                    Retcode = 0,
                    Message = "OK",
                    Data = new()
                    {
                        Account = new()
                        {
                            Uid = user.Uid,
                            Name = user.Name,
                            Email = "",
                            Mobile = "",
                            IsEmailVerify = "0",
                            Realname = "",
                            IdentityCard = "",
                            Token = user.Token.ToString(),
                            SafeMobile = "",
                            FacebookName = "",
                            GoogleName = "",
                            TwitterName = "",
                            GameCenterName = "",
                            AppleName = "",
                            SonyName = "",
                            TapName = "",
                            Country = "**",
                            ReactivateTicket = "",
                            AreaCode = "**",
                            DeviceGrantTicket = "",
                            SteamName = "",
                            UnmaskedEmail = "",
                            UnmaskedEmailType = 0
                        },
                        DeviceGrantRequired = false,
                        SafeMoblieRequired = false,
                        RealpersonRequired = false,
                        ReactivateRequired = false,
                        RealnameOperation = "None"
                    }
                };

                ctx.Response.Headers.Add("Content-Type", "application/json");

                return ctx.Response.WriteAsync(JsonConvert.SerializeObject(rsp));
            });
#pragma warning restore CS8600, CS8602 // Converting null literal or possible null value to non-nullable type.
        }
    }
}
