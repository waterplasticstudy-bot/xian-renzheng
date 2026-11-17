using System;
using System.Collections.Generic;

namespace XianAuth
{
    public sealed class ExtractRule
    {
        public string Name { get; set; }
        public string Pattern { get; set; }
    }

    public sealed class TemplateStep
    {
        public TemplateStep()
        {
            Extract = new List<ExtractRule>();
        }

        public string Method { get; set; }
        public string Url { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public List<ExtractRule> Extract { get; set; }
    }

    public sealed class PortalProfile
    {
        public PortalProfile()
        {
            Ssids = new List<string>();
            UrlContains = new List<string>();
            Steps = new List<TemplateStep>();
            SuccessContains = new List<string>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string TemplateKind { get; set; }
        public string Notes { get; set; }
        public List<string> Ssids { get; set; }
        public List<string> UrlContains { get; set; }
        public DrcomOptions Drcom { get; set; }
        public SrunOptions Srun { get; set; }
        public RuijieOptions Ruijie { get; set; }
        public List<TemplateStep> Steps { get; set; }
        public List<string> SuccessContains { get; set; }
        public string LogoutUrl { get; set; }
        public string LogoutMethod { get; set; }
        public string LogoutBody { get; set; }
        public string LogoutContentType { get; set; }
    }

    public static class PortalSelector
    {
        public static PortalProfile Select(IList<PortalProfile> profiles, string activeId, string ssid, string redirectUrl)
        {
            PortalProfile best = null;
            int bestScore = 0;
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    if (profile == null)
                        continue;
                    int score = Score(profile, ssid, redirectUrl);
                    if (score > 0 && string.Equals(profile.Id, activeId, StringComparison.Ordinal))
                        score += 25;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = profile;
                    }
                }
            }
            if (bestScore >= 50)
                return best;

            var finger = Synthesize(ssid, redirectUrl);
            if (finger != null)
                return finger;
            return bestScore > 0 ? best : null;
        }

        public static int Score(PortalProfile profile, string ssid, string redirectUrl)
        {
            int score = 0;
            if (!string.IsNullOrEmpty(ssid) && profile.Ssids != null)
            {
                for (int i = 0; i < profile.Ssids.Count; i++)
                {
                    if (string.Equals(profile.Ssids[i], ssid, StringComparison.Ordinal))
                    {
                        score += 100;
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(redirectUrl) && profile.UrlContains != null)
            {
                for (int i = 0; i < profile.UrlContains.Count; i++)
                {
                    string needle = profile.UrlContains[i];
                    if (!string.IsNullOrEmpty(needle)
                        && redirectUrl.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 60;
                }
            }
            return score;
        }

        public static PortalProfile Synthesize(string ssid, string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            if (DrcomEportal.LooksLike(url))
                return DrcomEportal.ProfileFromUrl(url, ssid);
            if (SrunPortal.LooksLike(url))
                return SrunFromUrl(url, ssid);
            if (RuijiePortal.LooksLike(url))
                return RuijieFromUrl(url, ssid);
            return null;
        }

        static PortalProfile SrunFromUrl(string url, string ssid)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return null;
            var query = DrcomEportal.ParseQuery(uri.Query);
            string acid;
            if (!query.TryGetValue("ac_id", out acid) || string.IsNullOrEmpty(acid))
                acid = "1";
            var profile = new PortalProfile
            {
                Id = "srun-" + uri.Host.Replace(".", "-"),
                Name = "深澜 srun",
                Kind = "srun",
                Srun = new SrunOptions
                {
                    BaseUrl = uri.GetLeftPart(UriPartial.Authority),
                    AcId = acid
                }
            };
            if (!string.IsNullOrEmpty(ssid))
                profile.Ssids.Add(ssid);
            profile.UrlContains.Add(uri.Host);
            profile.UrlContains.Add("srun");
            return profile;
        }

        static PortalProfile RuijieFromUrl(string url, string ssid)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return null;
            var profile = new PortalProfile
            {
                Id = "ruijie-" + uri.Host.Replace(".", "-"),
                Name = "锐捷 ePortal",
                Kind = "ruijie",
                Ruijie = new RuijieOptions
                {
                    BaseUrl = uri.GetLeftPart(UriPartial.Authority),
                    Service = ""
                }
            };
            if (!string.IsNullOrEmpty(ssid))
                profile.Ssids.Add(ssid);
            profile.UrlContains.Add(uri.Host);
            profile.UrlContains.Add("eportal/index.jsp");
            return profile;
        }
    }

    public static class PortalEndpoints
    {
        public static bool TryGetIpv4(PortalProfile profile, out string ip)
        {
            ip = null;
            string raw = null;
            if (profile != null)
            {
                if (profile.Drcom != null)
                    raw = profile.Drcom.BaseUrl;
                else if (profile.Srun != null)
                    raw = profile.Srun.BaseUrl;
                else if (profile.Ruijie != null)
                    raw = profile.Ruijie.BaseUrl;
                else if (profile.Steps != null && profile.Steps.Count > 0)
                    raw = profile.Steps[0].Url;
            }
            if (string.IsNullOrEmpty(raw))
                return false;
            Uri uri;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out uri))
                return ClashRules.TryNormalizeIpv4(raw, out ip);
            return ClashRules.TryNormalizeIpv4(uri.Host, out ip);
        }
    }

    public static class TemplateText
    {
        public static string Apply(string input, IDictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(input) || vars == null || vars.Count == 0)
                return input ?? "";
            var keys = new List<string>(vars.Keys);
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                string value;
                vars.TryGetValue(key, out value);
                input = input.Replace("{" + key + "}", value ?? "");
            }
            return input;
        }

        public static string ChineseKind(string templateKind)
        {
            switch (templateKind)
            {
                case "single-post":
                    return "单POST";
                case "double-post":
                    return "双POST";
                case "single-get":
                    return "GET";
                case "single-put":
                    return "PUT";
                default:
                    return templateKind ?? "";
            }
        }
    }
}
