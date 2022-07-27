﻿using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using ReserveBlockCore.Data;
using ReserveBlockCore.Models;
using ReserveBlockCore.P2P;
using ReserveBlockCore.Utilities;
using System.IO.Compression;

namespace ReserveBlockCore.Services
{
    public class ClientCallService : IHostedService, IDisposable
    {
        private readonly IHubContext<P2PAdjServer> _hubContext;
        private readonly IHostApplicationLifetime _appLifetime;
        private int executionCount = 0;
        private Timer _timer = null!;
        private Timer _fortisPoolTimer = null!;
        private Timer _checkpointTimer = null!;
        private Timer _blockStateSyncTimer = null;
        private static bool FirstRun = false;

        public ClientCallService(IHubContext<P2PAdjServer> hubContext, IHostApplicationLifetime appLifetime)
        {
            _hubContext = hubContext;
            _appLifetime = appLifetime;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _timer = new Timer(DoWork, null, TimeSpan.FromSeconds(180),
                TimeSpan.FromSeconds(2));

            _fortisPoolTimer = new Timer(DoFortisPoolWork, null, TimeSpan.FromSeconds(240),
                TimeSpan.FromMinutes(5));

            _blockStateSyncTimer = new Timer(DoBlockStateSyncWork, null, TimeSpan.FromSeconds(300),
                TimeSpan.FromMinutes(15));

            if (Program.ChainCheckPoint == true)
            {
                var interval = Program.ChainCheckPointInterval;
                
                _checkpointTimer = new Timer(DoCheckpointWork, null, TimeSpan.FromSeconds(240),
                TimeSpan.FromHours(interval));
            }

            return Task.CompletedTask;
        }

        private async void DoCheckpointWork(object? state)
        {
            var retain = Program.ChainCheckPointRetain;
            var path = GetPathUtility.GetDatabasePath();
            var checkpointPath = Program.ChainCheckpointLocation;
            var zipPath = checkpointPath + "checkpoint_" + DateTime.Now.Ticks.ToString();

            try
            {
                var directoryCount = Directory.GetFiles(checkpointPath).Length;
                if(directoryCount >= retain)
                {
                    FileSystemInfo fileInfo = new DirectoryInfo(checkpointPath).GetFileSystemInfos()
                        .OrderBy(fi => fi.CreationTime).First();
                    fileInfo.Delete();
                }

                ZipFile.CreateFromDirectory(path, zipPath);
                var createDate = DateTime.Now.ToString();
                LogUtility.Log($"Checkpoint successfully created at: {createDate}", "ClientCallService.DoCheckpointWork()");
            }
            catch(Exception ex)
            {
                ErrorLogUtility.LogError($"Error creating checkpoint. Error Message: {ex.Message}", "ClientCallService.DoCheckpointWork()");
            }
        }

        private async void DoBlockStateSyncWork(object? state)
        {
            await StateTreiSyncService.SyncAccountStateTrei();

        }
        private async void DoFortisPoolWork(object? state)
        {
            try
            {
                if (Program.StopAllTimers == false)
                {
                    if (Program.Adjudicate)
                    {
                        var currentTime = DateTime.Now.AddMinutes(-15);
                        var fortisPool = P2PAdjServer.FortisPool.Where(x => x.LastAnswerSendDate >= currentTime);

                        var fortisPoolStr = "";
                        fortisPoolStr = JsonConvert.SerializeObject(fortisPool);

                        var explorerNode = fortisPool.Where(x => x.Address == "RHNCRbgCs7KGdXk17pzRYAYPRKCkSMwasf").FirstOrDefault();

                        if (explorerNode != null)
                        {
                            await _hubContext.Clients.Client(explorerNode.ConnectionId).SendAsync("GetAdjMessage", "fortisPool", fortisPoolStr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //no node found
                Console.WriteLine("****************DoFortisPoolWork - Failed****************");
            }
        }
        private async void DoWork(object? state)
        {
            try
            {
                if (Program.StopAllTimers == false)
                {
                    if (Program.Adjudicate)
                    {
                        var fortisPool = P2PAdjServer.FortisPool;

                        if (fortisPool.Count() > 0)
                        {
                            if(FirstRun == false)
                            {
                                //
                                FirstRun = true;
                                Console.WriteLine("Doing the work");
                            }
                            //get last block timestamp and current timestamp if they are more than 1 mins apart start new task
                            var lastBlockSubmitUnixTime = Program.LastAdjudicateTime;
                            var currentUnixTime = TimeUtil.GetTime();
                            var timeDiff = (currentUnixTime - lastBlockSubmitUnixTime);

                            if (timeDiff > 28)
                            {
                                if (Program.AdjudicateLock == false)
                                {
                                    Program.AdjudicateLock = true;

                                    //once greater commit block winner
                                    var taskAnswerList = P2PAdjServer.TaskAnswerList;
                                    var taskQuestion = P2PAdjServer.CurrentTaskQuestion;
                                    List<TaskAnswer>? failedTaskAnswersList = null;

                                    if (taskAnswerList.Count() > 0)
                                    {
                                        ConsoleWriterService.Output("Beginning Solve. Received Answers: " + taskAnswerList.Count().ToString());
                                        bool findWinner = true;
                                        while (findWinner)
                                        {
                                            var taskWinner = await TaskWinnerUtility.TaskWinner(taskQuestion, taskAnswerList, failedTaskAnswersList);
                                            if (taskWinner != null)
                                            {
                                                ConsoleWriterService.Output("Task Winner was Found! " + taskWinner.Address);
                                                var nextBlock = taskWinner.Block;
                                                if (nextBlock != null)
                                                {
                                                    var result = await BlockValidatorService.ValidateBlock(nextBlock);
                                                    if (result == true)
                                                    {
                                                        ConsoleWriterService.Output("Task Completed and Block Found: " + nextBlock.Height.ToString());
                                                        ConsoleWriterService.Output(DateTime.Now.ToString());
                                                        string data = "";
                                                        data = JsonConvert.SerializeObject(nextBlock);
                                                        
                                                        await _hubContext.Clients.All.SendAsync("GetAdjMessage", "taskResult", data);
                                                        ConsoleWriterService.Output("Sending Blocks Now - Height: " + nextBlock.Height.ToString());
                                                        //Update submit time to wait another 28 seconds to process.
                                                        

                                                        //send new puzzle and wait for next challenge completion
                                                        string taskQuestionStr = "";
                                                        var nTaskQuestion = await TaskQuestionUtility.CreateTaskQuestion("rndNum");
                                                        ConsoleWriterService.Output("New Task Created.");
                                                        P2PAdjServer.CurrentTaskQuestion = nTaskQuestion;
                                                        TaskQuestion nSTaskQuestion = new TaskQuestion();
                                                        nSTaskQuestion.TaskType = nTaskQuestion.TaskType;
                                                        nSTaskQuestion.BlockHeight = nTaskQuestion.BlockHeight;
                                                        
                                                        taskQuestionStr = JsonConvert.SerializeObject(nSTaskQuestion);


                                                        await ProcessFortisPool(taskAnswerList);
                                                        ConsoleWriterService.Output("Fortis Pool Processed");

                                                        if(P2PAdjServer.TaskAnswerList != null)
                                                        {
                                                            //P2PAdjServer.TaskAnswerList.Clear();
                                                            //P2PAdjServer.TaskAnswerList.TrimExcess();
                                                            P2PAdjServer.TaskAnswerList.RemoveAll(x => x.Block.Height <= nextBlock.Height);
                                                        }

                                                        Thread.Sleep(1000);

                                                        await _hubContext.Clients.All.SendAsync("GetAdjMessage", "task", taskQuestionStr);
                                                        ConsoleWriterService.Output("Task Sent.");

                                                        findWinner = false;
                                                        Program.AdjudicateLock = false;
                                                        Program.LastAdjudicateTime = TimeUtil.GetTime();

                                                        P2PAdjServer.BroadcastedTrxList = new List<Transaction>();
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Block failed validation");
                                                        if (failedTaskAnswersList == null)
                                                        {
                                                            failedTaskAnswersList = new List<TaskAnswer>();
                                                        }
                                                        failedTaskAnswersList.Add(taskWinner);
                                                    }
                                                }

                                            }
                                            else
                                            {
                                                ConsoleWriterService.Output("Task Winner was Not Found!");
                                                if (failedTaskAnswersList != null)
                                                {
                                                    List<TaskAnswer> validTaskAnswerList = taskAnswerList.Except(failedTaskAnswersList).ToList();
                                                    if (validTaskAnswerList.Count() == 0)
                                                    {
                                                        ConsoleWriterService.Output("Error in task list");
                                                        //If this happens that means not a single task answer yielded a validatable block.
                                                        //If this happens chain must be corrupt or zero validators are online.
                                                        findWinner = false;
                                                        Program.AdjudicateLock = false;
                                                    }
                                                }
                                                else
                                                {
                                                    ConsoleWriterService.Output("Task list failed to find winner");
                                                    failedTaskAnswersList = new List<TaskAnswer>();
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {

                                        Program.AdjudicateLock = false;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        //dipose timer.
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                Console.WriteLine("Client Call Service");
                Program.AdjudicateLock = false;
            }
            
        }

        public async Task ProcessFortisPool(List<TaskAnswer> taskAnswerList)
        {
            try
            {
                var pool = P2PAdjServer.FortisPool;
                var result = pool.GroupBy(x => x.Address).Where(x => x.Count() > 1).Select(y => y.OrderByDescending(z => z.ConnectDate).ToList()).ToList();

                if (result.Count() > 0)
                {
                    result.ForEach(x =>
                    {
                        var recKeep = x.First();
                        P2PAdjServer.FortisPool.RemoveAll(f => f.ConnectionId != recKeep.ConnectionId && f.Address == recKeep.Address);
                    });
                }

                if (taskAnswerList != null)
                {
                    foreach (TaskAnswer taskAnswer in taskAnswerList)
                    {
                        var validator = P2PAdjServer.FortisPool.Where(x => x.Address == taskAnswer.Address).FirstOrDefault();
                        {
                            if (validator != null)
                            {
                                validator.LastAnswerSendDate = DateTime.Now;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: ClientCallService.ProcessFortisPool: " + ex.Message);
            }

        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer.Dispose();
            _fortisPoolTimer.Dispose();
            _blockStateSyncTimer.Dispose();
            _checkpointTimer.Dispose();
        }

        public async Task SendMessage(string message, string data)
        {
            await _hubContext.Clients.All.SendAsync("GetAdjMessage", message, data);
        }
    }
}
