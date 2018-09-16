using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using System.Xml.XPath;

// TODO: Support Windows 7 not showing file extensions.

namespace WTVRenamer
{
    class Program
    {
        private static APIDatabase[] databases = new[]
        {
            new APIDatabase(){ baseUrl = "http://thetvdb.com", seriesUrl = "/api/GetSeries.php?seriesname=$seriesName", episodeUrl = "/api/$apikey/series/$this_series_id/all/$this_series_lang.xml" },
            new APIDatabase(){ baseUrl = "http://www.omdbapi.com/" },
        };

        public const string configFile = "WTVRenamer.cfg";
        public const string logFile = "WTVRenamer.log";
        private static string inputDirectory = ".";
        private static string tvShowDirectory = ".";
        private static string movieDirectory = ".";

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                ReadConfiguration();

                Log("----- WTVRenamer Started! --------------------");
                List<RecordedShow> recordedShows = new List<RecordedShow>();
                foreach (var item in Directory.GetFiles(inputDirectory, "*.wtv"))
                {
                    var show = CreateShow(item);
                    if (show != null)
                        recordedShows.Add(show);

                    Console.WriteLine(show);
                }

                foreach (var show in recordedShows)
                    if (show.recommendedFilename != show.originalFilename)
                    {
                        var from = Path.Combine(inputDirectory, show.originalFilename);
                        var to = Path.Combine(inputDirectory, show.recommendedFilename);
                        Log("Renaming from {0} to {1}", from, to);
                        try { File.Move(from, to); }
                        catch (Exception ex) { Log("Could not rename {0}: {1}", from, ex.Message); }
                    }

                foreach (var show in recordedShows)
                    if (show.showType == RecordedShow.ShowType.TVSHOW)
                        if (tvShowDirectory != "." && !string.IsNullOrEmpty(tvShowDirectory))
                        {
                            var from = Path.Combine(inputDirectory, show.recommendedFilename);
                            var to = Path.Combine(tvShowDirectory, show.recommendedFilename);
                            Log("Moving from {0} to {1}", from, to);
                            try { File.Move(from, to); }
                            catch (Exception ex) { Log("Could not move {0}: {1}", from, ex.Message); }
                        }
                        else { }
                    else if (show.showType == RecordedShow.ShowType.MOVIE)
                        if (movieDirectory != "." && !string.IsNullOrEmpty(movieDirectory))
                        {
                            var from = Path.Combine(inputDirectory, show.recommendedFilename);
                            var to = Path.Combine(movieDirectory, show.recommendedFilename);
                            Log("Moving from {0} to {1}", from, to);
                            try { File.Move(from, to); }
                            catch (Exception ex) { Log("Could not move {0}: {1}", from, ex.Message); }
                        }

                Log("----- WTVRenamer Ended! ----------------------");
            }
            catch(Exception ex)
            {
                Log(ex.Message);
                Log(ex.StackTrace);
                Log("----- WTVRenamer Program Error ----------------------");
            }
        }

        private static void Log(string format, params object[] args)
        {
            var log = string.Format(format, args);
            Console.WriteLine(log);
            File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + log + "\r\n");
        }

        private static void ReadConfiguration()
        {
            if (!File.Exists(configFile))
                File.WriteAllText(configFile, string.Format(@"inputDirectory: {0}
tvShowDirectory: {1}
movieDirectory: {2}", inputDirectory, tvShowDirectory, movieDirectory));

            var lines = File.ReadAllLines(configFile);
            foreach (var line in lines)
            {
                var colonSeperated = line.Split(':');
                if (colonSeperated.Length > 1)
                    if (colonSeperated[0].ToLower().Equals("inputdirectory"))
                        inputDirectory = line.Remove(0, line.IndexOf(':') + 1).Trim();
                    else if (colonSeperated[0].ToLower().Equals("tvshowdirectory"))
                        tvShowDirectory = line.Remove(0, line.IndexOf(':') + 1).Trim();
                    else if (colonSeperated[0].ToLower().Equals("moviedirectory"))
                        movieDirectory = line.Remove(0, line.IndexOf(':') + 1).Trim();
            }

            if (inputDirectory == "." || inputDirectory == @".\")
                inputDirectory = Directory.GetCurrentDirectory();
        }

        private static RecordedShow CreateShow(string path)
        {
            var item = ShellFile.FromFilePath(path);

            var newShow = new RecordedShow
            {
                originalFilename = item.Name,
                
                seriesName = item.Properties.System.Title.Value,
                episodeName = item.Properties.System.RecordedTV.EpisodeName.Value,
                description = item.Properties.System.RecordedTV.ProgramDescription.Value,
                stationCallSign = item.Properties.System.RecordedTV.StationCallSign.Value
            };
            try { newShow.episodeFirstAired = ((DateTime)item.Properties.System.RecordedTV.OriginalBroadcastDate.Value).ToString("yyyy-MM-dd"); } catch { }

            var xml = DownloadSeriesXML(newShow.seriesName);
            var xDoc = XDocument.Parse(xml);
            try
            {
                var seriesIds = xDoc.XPathSelectElements("/Data/Series/seriesid");
                if (seriesIds.Count() == 0)
                    newShow.showType = RecordedShow.ShowType.MOVIE;

                for (int i = 0; i < seriesIds.Count(); i++)
                {
                    if (i == 0)
                        newShow.seriesId = newShow.id = seriesIds.ElementAt(i).Value;
                    else if (i == 1)
                        newShow.alternateSeriesId = seriesIds.ElementAt(i).Value;
                }
            }
            catch { newShow.showType = RecordedShow.ShowType.MOVIE; }
            try { newShow.language = xDoc.XPathSelectElement("/Data/Series/language").Value; } catch { }
            try { newShow.network = xDoc.XPathSelectElement("/Data/Series/Network").Value; } catch { }
            try { newShow.imdbId = xDoc.XPathSelectElement("/Data/Series/IMDB_ID").Value; } catch { }
            try { newShow.zap2itId = xDoc.XPathSelectElement("/Data/Series/zap2it_id").Value; } catch { }
            try { newShow.seriesFirstAired = xDoc.XPathSelectElement("/Data/Series/FirstAired").Value; } catch { }

            if (newShow.showType == RecordedShow.ShowType.TVSHOW && newShow.episodeName != null)
            {
                xml = DownloadEpisodeXML(newShow);
                xDoc = XDocument.Parse(xml);

                foreach (var episode in xDoc.XPathSelectElements("/Data/Episode"))
                {
                    if (episode.XPathSelectElement("EpisodeName").Value.ToLower().Equals(newShow.episodeName.ToLower()))
                    {
                        newShow.episodeNumber = episode.XPathSelectElement("EpisodeNumber").Value;
                        newShow.seasonNumber = episode.XPathSelectElement("SeasonNumber").Value;
                        break;
                    }
                }

                if (newShow.episodeNumber == null && newShow.seasonNumber == null)
                {
                    foreach (var episode in xDoc.XPathSelectElements("/Data/Episode"))
                    {
                        if (episode.XPathSelectElement("FirstAired").Value.Equals(newShow.episodeFirstAired))
                        {
                            newShow.episodeNumber = episode.XPathSelectElement("EpisodeNumber").Value;
                            newShow.seasonNumber = episode.XPathSelectElement("SeasonNumber").Value;
                            break;
                        }
                    }
                }

                if (newShow.episodeNumber == null && newShow.seasonNumber == null)
                {
                    try
                    {
                        xml = DownloadAlternateEpisodeXML(newShow);
                        xDoc = XDocument.Parse(xml);

                        foreach (var episode in xDoc.XPathSelectElements("/Data/Episode"))
                        {
                            if (episode.XPathSelectElement("EpisodeName").Value.ToLower().Equals(newShow.episodeName.ToLower()))
                            {
                                newShow.episodeNumber = episode.XPathSelectElement("EpisodeNumber").Value;
                                newShow.seasonNumber = episode.XPathSelectElement("SeasonNumber").Value;
                                break;
                            }
                        }

                        if (newShow.episodeNumber == null && newShow.seasonNumber == null)
                        {
                            foreach (var episode in xDoc.XPathSelectElements("/Data/Episode"))
                            {
                                if (episode.XPathSelectElement("FirstAired").Value.Equals(newShow.episodeFirstAired))
                                {
                                    newShow.episodeNumber = episode.XPathSelectElement("EpisodeNumber").Value;
                                    newShow.seasonNumber = episode.XPathSelectElement("SeasonNumber").Value;
                                    break;
                                }
                            }
                        }
                    }
                    catch { Log("Could not download DownloadAlternateEpisodeXML()"); }
                }
            }

            if (newShow.episodeNumber != null && newShow.seasonNumber != null)
                newShow.recommendedFilename = newShow.seriesName.Replace(':', '-') + " - S" + newShow.SeasonNumber + "E" + newShow.EpisodeNumber + ".wtv";
            else
                newShow.recommendedFilename = newShow.originalFilename;

            return newShow;
        }

        private static string DownloadSeriesXML(string seriesName)
        {
            var url = databases[0].baseUrl + databases[0].seriesUrl;
            url = url.Replace("$seriesName", seriesName);
            Log("DownloadSeriesXML from {0}", url);
            return new WebClient().DownloadString(url);
        }

        private static string DownloadEpisodeXML(RecordedShow recordedShow)
        {
            var url = databases[0].baseUrl + databases[0].episodeUrl;
            url = url.Replace("$apikey", recordedShow.apiKey);
            url = url.Replace("$this_series_id", recordedShow.seriesId);
            url = url.Replace("$this_series_lang", recordedShow.language);
            Log("DownloadEpisodeXML from {0}", url);
            return new WebClient().DownloadString(url);
        }

        private static string DownloadAlternateEpisodeXML(RecordedShow recordedShow)
        {
            var url = databases[0].baseUrl + databases[0].episodeUrl;
            url = url.Replace("$apikey", recordedShow.apiKey);
            url = url.Replace("$this_series_id", recordedShow.alternateSeriesId);
            url = url.Replace("$this_series_lang", recordedShow.language);
            Log("DownloadAlternateEpisodeXML from {0}", url);
            return new WebClient().DownloadString(url);
        }

        public class APIDatabase
        {
            public string baseUrl;
            public string seriesUrl;
            public string episodeUrl;
        }

        public class RecordedShow
        {
            public enum ShowType { TVSHOW, MOVIE }
            public ShowType showType;

            public readonly string apiKey = "700446549F94A042";
            public string seriesId;
            public string alternateSeriesId;
            public string language;
            public string seriesName;
            public string episodeName;
            public string seriesFirstAired;
            public string episodeFirstAired;
            public string network;
            public string imdbId;
            public string zap2itId;
            public string id;
            public string description;
            public string stationCallSign;
            public string episodeNumber;
            public string seasonNumber;
            public string originalFilename;
            public string recommendedFilename;

            public string EpisodeNumber { get { return Convert.ToInt32(episodeNumber).ToString("00"); } }
            public string SeasonNumber { get { return Convert.ToInt32(seasonNumber).ToString("00"); } }

            public override string ToString()
            {
                var output = "";
                var fields = GetType().GetFields();
                foreach (var field in fields)
                    output += string.Format("{0}: {1}\r\n", field.Name, field.GetValue(this));
                return output;
            }
        }
    }
}
