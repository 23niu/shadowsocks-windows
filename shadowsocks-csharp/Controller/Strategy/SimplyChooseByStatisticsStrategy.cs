﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Shadowsocks.Model;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;

namespace Shadowsocks.Controller.Strategy
{
    class SimplyChooseByStatisticsStrategy : IStrategy
    {
        private ShadowsocksController _controller;
        private Server _currentServer;
        private Timer timer;
        private Dictionary<string, StatisticsData> statistics;
        private static readonly int CachedInterval = 60 * 60 * 1000; //choose a new server every 60 minutes

        public SimplyChooseByStatisticsStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            _currentServer = null;  //we can also choose a server randomly at first
            timer = new Timer(ReloadStatisticsAndChooseAServer);
        }

        private void ReloadStatisticsAndChooseAServer(object obj)
        {
            Logging.Debug("Reloading statistics and choose a new server....");
            List<Server> servers = _controller.GetCurrentConfiguration().configs;
            LoadStatistics();
            ChooseNewServer(servers);
        }

        /*
        return a dict:
        {
            'ServerFriendlyName1':StatisticsData,
            'ServerFriendlyName2':...
        }
        */
        private void LoadStatistics()
        {
            try
            {
                var path = AvailabilityStatistics.AvailabilityStatisticsFile;
                Logging.Debug(string.Format("loading statistics from{0}", path));
                statistics = (from l in File.ReadAllLines(path)
                                  .Skip(1)
                                  let strings = l.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                                  let rawData = new
                                  {
                                      ServerName = strings[1],
                                      IPStatus = strings[2],
                                      RoundtripTime = int.Parse(strings[3])
                                  }
                                  group rawData by rawData.ServerName into server
                                  select new
                                  {
                                      ServerName = server.Key,
                                      data = new StatisticsData
                                      {
                                          SuccessTimes = server.Count(data => IPStatus.Success.ToString().Equals(data.IPStatus)),
                                          TimedOutTimes = server.Count(data => IPStatus.TimedOut.ToString().Equals(data.IPStatus)),
                                          AverageResponse = Convert.ToInt32(server.Average(data => data.RoundtripTime)),
                                          MinResponse = server.Min(data => data.RoundtripTime),
                                          MaxResponse = server.Max(data => data.RoundtripTime)
                                      }
                                  }).ToDictionary(server => server.ServerName, server => server.data);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
        }

        //return the score by data
        //server with highest score will be choosen
        private static double GetScore(StatisticsData data)
        {
            return (double)data.SuccessTimes / (data.SuccessTimes + data.TimedOutTimes); //simply choose min package loss
        }

        private class StatisticsData
        {
            public int SuccessTimes;
            public int TimedOutTimes;
            public int AverageResponse;
            public int MinResponse;
            public int MaxResponse;
        }

        private void ChooseNewServer(List<Server> servers)
        {
            try
            {
                var bestResult = (from server in servers
                                  let name = server.FriendlyName()
                                  where statistics.ContainsKey(name)
                                  select new
                                  {
                                      server,
                                      score = GetScore(statistics[name])
                                  }
                                  ).Aggregate((result1, result2) => result1.score > result2.score ? result1 : result2);

                Logging.Debug(string.Format("best server {0}: {1}", bestResult.server.FriendlyName(), bestResult.score));
                _currentServer = bestResult.server;
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
        }

        string IStrategy.ID
        {
            get { return "com.shadowsocks.strategy.scbs"; }
        }

        string IStrategy.Name
        {
            get { return I18N.GetString("Simply Choose By Statics"); }
        }

        Server IStrategy.GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint)
        {
            Console.WriteLine("Switch to server by statistics: {0}", _currentServer.FriendlyName());
            return _currentServer;  //current server cached for CachedInterval
        }

        void IStrategy.ReloadServers()
        {
            ChooseNewServer(_controller.GetCurrentConfiguration().configs);
            timer?.Change(0, CachedInterval);
        }

        void IStrategy.SetFailure(Server server)
        {
            Logging.Debug(String.Format("failure: {0}", server.FriendlyName()));
        }

        void IStrategy.UpdateLastRead(Server server)
        {
            //TODO: combine this part of data with ICMP statics
        }

        void IStrategy.UpdateLastWrite(Server server)
        {
            //TODO: combine this part of data with ICMP statics
        }

        void IStrategy.UpdateLatency(Server server, TimeSpan latency)
        {
            //TODO: combine this part of data with ICMP statics
        }

    }
}
