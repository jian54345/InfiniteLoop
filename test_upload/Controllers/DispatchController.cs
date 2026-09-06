using HttpServer.Models;
using Newtonsoft.Json;
using Common;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;

namespace HttpServer.Controllers
{
#pragma warning disable CS8601 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public class DispatchController
    {
        public static ExtraConfig ExtraConfig = JsonConvert.DeserializeObject<ExtraConfig>(File.ReadAllText("extraconfig.json"));

        public static void AddHandlers(WebApplication app)
        {
            app.Map("/query_dispatch", (ctx) =>
            {
                QueryDispatch rsp = new()
                {
                    Retcode = 0,
                    RegionList = new Region[] {
                        new Region() {
                            Retcode = 0,
                            DispatchUrl = $"http://{Global.config.Gameserver.Host}/query_gateway",
                            Name = Global.config.Gameserver.RegionName,
                            Title = "",
                        }
                    }
                };

                ctx.Response.Headers.Add("Content-Type", "application/json");
                return ctx.Response.WriteAsync(AesEncrypt(ctx.Request.Query["version"].ToString(), JsonConvert.SerializeObject(rsp)));
            });

            app.Map("/query_gateway", (ctx) =>
            {
                string Version = ctx.Request.Query["version"].ToString();
                Gameserver Gameserver = new()
                {
                    Ip = Global.config.Gameserver.Host,
                    Port = Global.config.Gameserver.Port
                };
                Manifest manifest = ExtraConfig.Versions.FirstOrDefault(x => Version.Contains(x.Key)).Value;

                QueryGateway rsp = new()
                {
                    Retcode = 0,
                    Msg = "",
                    RegionName = Global.config.Gameserver.RegionName,
                    AccountUrl = $"http://{Global.config.Gameserver.Host}/account",
                    AccountUrlBackup = $"http://{Global.config.Gameserver.Host}/account",
                    AssetBundleUrlList = GetAssetBundleUrlList(Version),
                    ExAudioAndVideoUrlList = new string[0],
                    ExResourceUrlList = GetExResourceUrlList(Version),
                    Ext = GetExt(Version),
                    Gameserver = Gameserver,
                    Gateway = Gameserver,
                    IsDataReady = true,
                    OaserverUrl = $"http://{Global.config.Gameserver.Host}/oaserver",
                    ServerCurTime = Global.GetUnixInSeconds(),
                    ServerCurTimezone = 8,
                    ServerExt = new ServerExt()
                    {
                        CdkeyUrl = $"http://{Global.config.Gameserver.Host}/common",
                        IsOfficial = "1",
                        MihoyoSdkEnv = "2"
                    },
                    Manifest = manifest
                };

                ctx.Response.Headers.Add("Content-Type", "application/json");
                return ctx.Response.WriteAsync(AesEncrypt(ctx.Request.Query["version"].ToString(), JsonConvert.SerializeObject(rsp)));
            });
        }

        public static Object GetExt(string version)
        {
            MatchCollection matches = Regex.Matches(version, @"\d+");
            var type = uint.Parse(string.Join("", matches));
            if (type < 730)
            {
                return new Ext()
                {
                    AiUseAssetBoundle = "1",
                    ApmLogLevel = "2",
                    ApmSwitch = "1",
                    ApmSwitchCrash = "1",
                    DataUseAssetBoundle = "1",
                    EnableWatermark = "1",
                    ExAudioAndVideoUrlList = GetExResourceUrlList(version),
                    ExResPrePublish = "0",
                    ExResUseHttp = "0",
                    ExResourceUrlList = GetExResourceUrlList(version),
                    ForbidRecharge = "1",
                    IsChecksumOff = "1",
                    OfflineReportSwitch = "1",
                    ResUseAssetBoundle = "1",
                    ShowVersionText = "0",
                    UpdateStreamingAsb = "1",
                    UseMultyCdn = "1",
                    ApmLogDest = "2",
                    ApmSwitchGameLog = "1",
                    BlockErrorDialog = "1",
                    ElevatorModelPath = "GameEntry/EVA/StartLoading_Model",
                    ExResBuffSize = "10485760",
                    IsXxxx = "0",
                    MtpSwitch = "0",
                    NetworkFeedbackEnable = "0",
                    ShowBulletinButton = "0",
                    ShowBulletinEmptyDialogBg = "0"
                };
            }

            var ext = ExtraConfig.Exts.FirstOrDefault(x => version.Contains(x.Key)).Value;
            if (ext is not null) return ext;

            return ExtraConfig.Exts.FirstOrDefault(x => "gf".Contains(x.Key)).Value;
        }

        public static string[] GetAssetBundleUrlList(string version)
        {
            Regex regex = new Regex(@"^(.*?)_(os|gf|global|jp|kr)_(.*?)$");
            Match matches = regex.Match(version);

            if (matches.Success)
            {
                string type = matches.Groups[2].Value; // get the second group (os or gf)

                switch (type)
                {
                    case "os":
                        return Global.config.UseLocalCache ?
                        [
                            $"http://{Global.config.Gameserver.Host}/asset_bundle/overseas01/1.1",
                            $"http://{Global.config.Gameserver.Host}/asset_bundle/overseas01/1.1"
                        ] :
                        [
                            "https://autopatchos.honkaiimpact3.com/asset_bundle/overseas01/1.1",
                            "https://bundle-aliyun-os.honkaiimpact3.com/asset_bundle/overseas01/1.1"
                        ];
                    case "gf":
                        if (version.Contains("beta"))
                        {
                            return Global.config.UseLocalCache ?
                            [
                                $"https://{Global.config.Gameserver.Host}/asset_bundle/beta_dev/1.0",
                                $"https://{Global.config.Gameserver.Host}/asset_bundle/beta_release/1.0"
                            ] :
                            [
                                "https://autopatchbeta.bh3.com/asset_bundle/beta_dev/1.0",
                                "https://bh3rd-beta.bh3.com/asset_bundle/beta_release/1.0",
                            ];
                        }
                        if (version.Contains("android"))
                        {
                            return Global.config.UseLocalCache ?
                            [
                                $"https://{Global.config.Gameserver.Host}/asset_bundle/android01/1.0",
                                $"https://{Global.config.Gameserver.Host}/asset_bundle/android01/1.0"
                            ] :
                            [
                                "https://autopatchcn.bh3.com/asset_bundle/android01/1.0",
                                "https://bundle.bh3.com/asset_bundle/android01/1.0"
                            ];
                        }
                        return Global.config.UseLocalCache ?
                        [
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/hun02/1.0",
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/hun02/1.0"
                        ] :
                        [
                            "https://autopatchcn.bh3.com/asset_bundle/hun02/1.0",
                            "https://bundle.bh3.com/asset_bundle/hun02/1.0"
                        ];
                    case "global":
                        return Global.config.UseLocalCache ?
                        [
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/usa01/1.1",
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/usa01/1.1"
                        ] :
                        [
                            "https://autopatchglb.honkaiimpact3.com/asset_bundle/usa01/1.1",
                            "http://bundle-aliyun-usa.honkaiimpact3.com/asset_bundle/usa01/1.1"
                        ];
                    case "jp":
                        return Global.config.UseLocalCache ?
                        [
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/jp01/1.1",
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/jp01/1.1"
                        ] : new string[]
                        {
                            "https://autopatchjp.honkaiimpact3.com/asset_bundle/jp01/1.1",
                            "https://bundle-aliyun-jp.honkaiimpact3.com/asset_bundle/jp01/1.1"
                        };
                    case "kr":
                        return Global.config.UseLocalCache ?
                        [
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/kr01/1.1",
                            $"https://{Global.config.Gameserver.Host}/asset_bundle/kr01/1.1"
                        ] :
                        [
                            "https://autopatchkr.honkaiimpact3.com/asset_bundle/kr01/1.1",
                            "https://bundle-aliyun-kr.honkaiimpact3.com/asset_bundle/kr01/1.1"
                        ];
                    default:
                        return Global.config.UseLocalCache ?
                        [
                            $"http://{Global.config.Gameserver.Host}/asset_bundle/overseas01/1.1",
                            $"http://{Global.config.Gameserver.Host}/asset_bundle/overseas01/1.1"
                        ] :
                        [
                            "https://autopatchos.honkaiimpact3.com/asset_bundle/overseas01/1.1",
                            "https://bundle-aliyun-os.honkaiimpact3.com/asset_bundle/overseas01/1.1"
                        ];
                }
            }
            else
            {
                return Global.config.UseLocalCache ?
                [
                    $"http://{Global.config.Gameserver.Host}/asset_bundle/overseas01/1.1",
                    $"http://{Global.config.Gameserver.Host}/asset_bundle/overseas01/1.1"
                ] :
                [
                    "https://autopatchos.honkaiimpact3.com/asset_bundle/overseas01/1.1",
                    "https://bundle-aliyun-os.honkaiimpact3.com/asset_bundle/overseas01/1.1"
                ];
            }
        }

        public static string[] GetExResourceUrlList(string version)
        {
            Regex regex = new(@"^(.*?)_(os|gf|global|jp|kr)_(.*?)$");
            Match matches = regex.Match(version);

            if (matches.Success)
            {
                string type = matches.Groups[2].Value; // get the second group (os or gf)

                switch (type)
                {
                    case "os":
                        return Global.config.UseLocalCache ? new string[]
                        {
                            $"{Global.config.Gameserver.Host}/com.miHoYo.bh3oversea",
                            $"{Global.config.Gameserver.Host}/com.miHoYo.bh3oversea"
                        } : new string[]
                        {
                            "autopatchos.honkaiimpact3.com/com.miHoYo.bh3oversea",
                            "bigfile-aliyun-os.honkaiimpact3.com/com.miHoYo.bh3oversea"
                        };
                    case "gf":
                        if (version.Contains("beta"))
                        {
                            return Global.config.UseLocalCache ? new string[]
                            {
                                $"{Global.config.Gameserver.Host}/tmp/beta",
                                $"{Global.config.Gameserver.Host}/tmp/beta"
                            } : new string[]
                            {
                                "autopatchbeta.bh3.com/tmp/beta",
                                "bh3rd-beta.bh3.com/tmp/beta",
                            };
                        }
                        return Global.config.UseLocalCache ? new string[]
                        {
                            $"{Global.config.Gameserver.Host}/tmp/Original",
                            $"{Global.config.Gameserver.Host}/tmp/Original"
                        } : new string[]
                        {
                            "autopatchcn.bh3.com/tmp/Original",
                            "bundle.bh3.com/tmp/Original",
                        };
                    case "global":
                        return Global.config.UseLocalCache ? new string[]
                        {
                            $"{Global.config.Gameserver.Host}/tmp/com.miHoYo.bh3global",
                            $"{Global.config.Gameserver.Host}/tmp/com.miHoYo.bh3global"
                        } : new string[]
                        {
                            "autopatchglb.honkaiimpact3.com/tmp/com.miHoYo.bh3global",
                            "bigfile-aliyun-usa.honkaiimpact3.com/tmp/com.miHoYo.bh3global"
                        };
                    case "jp":
                        return Global.config.UseLocalCache ? new string[]
                        {
                            $"{Global.config.Gameserver.Host}/tmp/com.miHoYo.bh3rdJP",
                            $"{Global.config.Gameserver.Host}/tmp/com.miHoYo.bh3rdJP"
                        } : new string[]
                        {
                            "autopatchjp.honkaiimpact3.com/tmp/com.miHoYo.bh3rdJP",
                            "bigfile-aliyun-jp.honkaiimpact3.com/tmp/com.miHoYo.bh3rdJP"
                        };
                    case "kr":
                        return Global.config.UseLocalCache ? new string[]
                        {
                            $"{Global.config.Gameserver.Host}/tmp/com.miHoYo.bh3korea",
                            $"{Global.config.Gameserver.Host}/tmp/com.miHoYo.bh3korea"
                        } : new string[]
                        {
                            "autopatchkr.honkaiimpact3.com/com.miHoYo.bh3korea",
                            "bigfile-aliyun-kr.honkaiimpact3.com/com.miHoYo.bh3korea"
                        };
                    default:
                        return Global.config.UseLocalCache ? new string[]
                        {
                            $"{Global.config.Gameserver.Host}/com.miHoYo.bh3oversea",
                            $"{Global.config.Gameserver.Host}/com.miHoYo.bh3oversea"
                        } : new string[]
                        {
                            "autopatchos.honkaiimpact3.com/com.miHoYo.bh3oversea",
                            "bigfile-aliyun-os.honkaiimpact3.com/com.miHoYo.bh3oversea"
                        };
                }
            }
            else
            {
                return Global.config.UseLocalCache ? new string[]
                {
                    $"{Global.config.Gameserver.Host}/com.miHoYo.bh3oversea",
                    $"{Global.config.Gameserver.Host}/com.miHoYo.bh3oversea"
                } : new string[]
                {
                    "autopatchos.honkaiimpact3.com/com.miHoYo.bh3oversea",
                    "bigfile-aliyun-os.honkaiimpact3.com/com.miHoYo.bh3oversea"
                };
            }
        }

        public static string AesEncrypt(string version, string rsp)
        {
            string key = ExtraConfig.Keys.FirstOrDefault(x => version.Contains(x.Key)).Value;
            if (key is null) return rsp;

            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.ECB;
                aes.Key = Encoding.UTF8.GetBytes(key);

                using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, null))
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(rsp);
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                        {
                            cryptoStream.Write(plainBytes, 0, plainBytes.Length);
                            cryptoStream.FlushFinalBlock();
                            return Convert.ToBase64String(memoryStream.ToArray());
                        }
                    }
                }
            }
        }
    }
}
