﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteDB;
using ReserveBlockCore.Data;
using ReserveBlockCore.Models;
using ReserveBlockCore.P2P;
using ReserveBlockCore.Utilities;
using Spectre.Console;

namespace ReserveBlockCore.Services
{
    internal class StartupService
    {
        internal static void StartupDatabase()
        {
            //Establish block, wallet, ban list, and peers db
            Console.WriteLine("Initializing Reserve Block Database...");
            if(Startup.IsTestNet == true)
            {
                DbContext.InitializeTest();
            }
            else
            {
                DbContext.Initialize();
            }
            
        }

        internal static void SetBlockchainChainRef()
        {
            //mainnet
            //BlockchainData.ChainRef = "m_Gi9RNxviAq1TmvuPZsZBzdAa8AWVJtNa7cm1dFaT4dWDbdqSNSTh";

            //testnet
            BlockchainData.ChainRef = "t2_Gi9RNxviAq1TmvuPZsZBzdAa8AWVJtNa7cm1dFaT4dWDbdqSNSTh";
        }

        //This is just for the initial launch of chain to help bootstrap known validators. This method will eventually be not needed.
        internal static void SetBootstrapValidators()
        {
            var validators = Validators.Validator.GetAll();

            var val1Check = validators.FindOne(x => x.Address == "RTX8Tg9PJMW6JTTdu7A5aKEDajawo9cr6g");

            if(val1Check == null)
            {
                var validator1 = new Validators
                {
                    Address = "RTX8Tg9PJMW6JTTdu7A5aKEDajawo9cr6g",
                    EligibleBlockStart = 0,
                    Amount = 3010M,
                    FailCount = 0,
                    IsActive = true,
                    NodeIP = "185.199.226.121",
                    Position = 1,
                    Signature = "MEQCIDVBdYv+Wfpil+j6d06JbCuWihrTUHP9xCqdAICVaVdXAiBpkyinNKZANOfz4rkao8KmzO461TevS5YGr8BNAdBBZg==.JTVCpmPPZMCTVyWhZzitGN4hnNT9YyhX5P6nMi15b8YezkrMsiygEnfMxCQdpwUjqwTsKdJBmjPt16NLaeFjnLR",
                    UniqueName = "GenesisValidator1"
                };

                validators.Insert(validator1);
            }

            var val2Check = validators.FindOne(x => x.Address == "RTC7uEaVWVakHwYQMhMDAyNkxYgjzV9WZq");

            if(val2Check == null)
            {
                var validator2 = new Validators
                {
                    Address = "RTC7uEaVWVakHwYQMhMDAyNkxYgjzV9WZq",
                    EligibleBlockStart = 0,
                    Amount = 1999M,
                    FailCount = 0,
                    IsActive = true,
                    NodeIP = "192.3.3.171",
                    Position = 2,
                    Signature = "MEUCIEVutYCQT5ruAKnh8BeLpNkx5lvKFji00H2R37IiO1YIAiEAgHuHBpcMb+2NJs8SMxCP05JGUQ2glB0bkgmQ9YEtBX0=.5mvvTz8QoF7FXwBufMjjhsyhhefAHcKHvLZQjb7FJqyaMq5JKofg8n8wJSf13kunqXDMWSU66aZCuSvbGpDRkbLZ",
                    UniqueName = "GenesisValidator2"
                };

                validators.Insert(validator2);
            }
        }

        internal static void StartupMemBlocks()
        {
            var blockChain = BlockchainData.GetBlocks();
            var blocks = blockChain.Find(Query.All(Query.Descending)).ToList();

            Program.MemBlocks = blocks.Take(15).ToList();
        }

        internal static async Task DownloadBlocksOnStart()
        {
            Program.StopAllTimers = true;
            var download = true;
            while(download) //this will loop forever till download happens
            {
                var result = await P2PClient.GetCurrentHeight();
                if (result.Item1 == true)
                {
                    Program.BlocksDownloading = true;
                    Program.BlocksDownloading = await BlockDownloadService.GetAllBlocks(result.Item2);                    
                }
                else
                {
                    Program.BlocksDownloading = false;
                    download = false; //exit the while. 
                    Program.StopAllTimers = false;
                    var accounts = AccountData.GetAccounts();
                    var accountList = accounts.FindAll().ToList();
                    if(accountList.Count() > 0)
                    {
                        var stateTrei = StateData.GetAccountStateTrei();
                        foreach(var account in accountList)
                        {
                            var stateRec = stateTrei.FindOne(x => x.Key == account.Address);
                            if(stateRec != null)
                            {
                                account.Balance = stateRec.Balance;
                                accounts.Update(account);//updating local record with synced state trei
                            }
                        }
                    }
                }
            }
            
        }

        internal static void CheckForDuplicateBlocks()
        {
            ///////////////////////////////////////////////////////////////////////
            //These methods will eventually no longer be needed once out of testnet.
            ClearSelfValidator();
            ResetEntireChain();
            //ResetChainToPoint();
            //
            ///////////////////////////////////////////////////////////////////////

            var blockChain = BlockchainData.GetBlocks();
            var blocks = blockChain.Find(Query.All(Query.Descending)).ToList();
            var dupBlocksList = blocks.GroupBy(x => x.Height).Where(y => y.Count() > 1).Select(z => z.Key).ToList();

            if(dupBlocksList.Count != 0)
            {
                //Reset blocks and all balances and redownload chain. No exception here.
                var accounts = AccountData.GetAccounts();
                var transactions = TransactionData.GetAll();
                var stateTrei = StateData.GetAccountStateTrei();
                var worldTrei = WorldTrei.GetWorldTrei();

                var accountList = accounts.FindAll();
                if(accountList.Count() > 0)
                {
                    foreach(var account in accountList)
                    {
                        account.Balance = 0.0M;
                        accounts.Update(account);//resets balances to 0.
                    }
                }

                transactions.DeleteAll();//delete all local transactions
                stateTrei.DeleteAll(); //removes all state trei data
                worldTrei.DeleteAll();  //removes the state trei
                blockChain.DeleteAll();//remove all blocks
                try
                {
                    DbContext.DB.Rebuild();
                    DbContext.DB_AccountStateTrei.Rebuild();
                    DbContext.DB_WorldStateTrei.Rebuild();
                    DbContext.DB_Wallet.Rebuild();

                }
                catch (Exception ex)
                {
                    //error saving from db cache
                }
            }
        }

        internal static void ResetEntireChain()
        {
            var blockChain = BlockchainData.GetBlocks();

            var genesisBlock = BlockchainData.GetGenesisBlock();

            if(genesisBlock.ChainRefId == "t_Gi9RNxviAq1TmvuPZsZBzdAa8AWVJtNa7cm1dFaT4dWDbdqSNSTh")
            {
                TransactionData.CreateGenesisTransction();

                TransactionData.GenesisTransactionsCreated = true;

                var accounts = AccountData.GetAccounts();
                var transactions = TransactionData.GetAll();
                var stateTrei = StateData.GetAccountStateTrei();
                var worldTrei = WorldTrei.GetWorldTrei();
                var validators = Validators.Validator.GetAll();
                var peers = Peers.GetAll();

                var accountList = accounts.FindAll();
                if (accountList.Count() > 0)
                {
                    foreach (var account in accountList)
                    {
                        account.Balance = 0.0M;
                        account.IsValidating = false;
                        accounts.Update(account);//resets balances to 0.
                    }
                }
                peers.DeleteAll();
                validators.DeleteAll();
                transactions.DeleteAll();//delete all local transactions
                stateTrei.DeleteAll(); //removes all state trei data
                worldTrei.DeleteAll();  //removes the state trei
                blockChain.DeleteAll();//remove all blocks

                try
                {
                    DbContext.DB.Rebuild();
                    DbContext.DB_AccountStateTrei.Rebuild();
                    DbContext.DB_WorldStateTrei.Rebuild();
                    DbContext.DB_Wallet.Rebuild();
                    DbContext.DB_Peers.Rebuild();

                    DbContext.DB.Checkpoint();
                    DbContext.DB_AccountStateTrei.Checkpoint();
                    DbContext.DB_WorldStateTrei.Checkpoint();
                    DbContext.DB_Wallet.Checkpoint();
                    DbContext.DB_Peers.Checkpoint();

                    
                }
                catch (Exception ex)
                {
                    //error saving from db cache
                }

                //re-add bootstrap validators
                SetBootstrapValidators();
            }
        }

        internal static void ResetChainToPoint()
        {
            var blockFixHeight = 19941;
            var blocks = BlockchainData.GetBlocks();
            var block = BlockchainData.GetBlockByHeight(blockFixHeight);
            int failCount = 0;
            if(block != null)
            {
                if(block.Hash == "baca9daedafe1b480927e6eefbd366380c0fa2191c444bd246d6f34b43393928")
                {
                    var stateTrei = StateData.GetAccountStateTrei();

                    stateTrei.DeleteAll();
                    DbContext.DB_AccountStateTrei.Checkpoint();

                    blocks.DeleteMany(x => x.Height >= blockFixHeight);
                    DbContext.DB.Checkpoint();
                    var blocksFromGenesis = blocks.Find(Query.All(Query.Ascending));

                    foreach (var blk in blocksFromGenesis)
                    {
                        var result = BlockchainRescanUtility.ValidateBlock(blk);
                        if(result == false)
                        {
                            failCount++;
                        }
                    }

                }
                else
                {
                    //do nothing
                }
            }

            if(failCount > 0)
            {
                Console.WriteLine("Resync Failed. Download whole chain.");
            }
            else
            {
                Console.WriteLine("Resync Completed.");
            }
        }

        internal static void ClearSelfValidator()
        {
            var validators = Validators.Validator.GetAll();
            var validator = validators.FindOne(x => x.NodeIP == "SELF");
            if (validator != null)
            {
                var accounts = AccountData.GetAccounts();
                var account = accounts.FindOne(x => x.Address == validator.Address);

                if(account != null)
                {
                    account.IsValidating = false;
                    accounts.Update(account);
                }
                var isDeleted = validators.Delete(validator.Id);
                if(isDeleted)
                {
                    DbContext.DB_Peers.Checkpoint();//commits from log file
                    //success
                }
            }
        }
        internal static async Task StartupPeers()
        {
            //add seed nodes
            SeedNodeService.SeedNodes();
            bool result = false;
            try
            {
                result = await P2PClient.ConnectToPeers();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            

            if(result == true)
            {
                //Connected to peers
                await BlockchainData.InitializeChain();
            }
            else
            {
                Console.WriteLine("Failed to automatically connect to peers. Please add manually.");
                //Put StartupInitializeChain();
                //Here and once chain fails to connect it will create genesis 
            }
        }
        internal static async Task<bool> DownloadBlocks() //download genesis block
        {
            var peersConnected = await P2PClient.ArePeersConnected();

            if (peersConnected.Item1)
            {
                var blocks = BlockData.GetBlocks();
                if(blocks.Count() == 0)
                {
                    Console.WriteLine("Downloading Blocks First.");
                    var blockCol = await P2PClient.GetBlock();

                    if(blockCol.Count() > 0)
                    {
                        foreach(var block in blockCol)
                        {
                            Console.WriteLine("Found Block: " + block.Height.ToString());
                            var result = await BlockValidatorService.ValidateBlock(block);
                            if (result == false)
                            {
                                Console.WriteLine("Block was rejected from: " + block.Validator);
                                //Add rejection notice for validator
                            }
                        }
                    }
                    
                }
                
            }
            return true;
        }
        internal static void StartupInitializeChain()
        {
            BlockchainData.InitializeChain();
        }
        internal static void StartupMenu()
        {
            Console.WriteLine("Starting up Reserve Block Wallet...");
            
            
            //Give thread a moment to recover.
            Thread.Sleep(1000);

            Console.WriteLine("Wallet Started. Awaiting Command...");
        }

        internal static void MainMenu()
        {
            Console.Clear();
            Console.SetCursorPosition(Console.CursorLeft, Console.CursorTop);

            AnsiConsole.Write(
                new FigletText("ReserveBlock Wallet")
                .LeftAligned()
                .Color(Color.Blue));

            Console.WriteLine("ReserverBlock Main Menu");
            Console.WriteLine("|======================================|");
            Console.WriteLine("| 1. Genesis Block (Check)             |");
            Console.WriteLine("| 2. Create Account                    |");
            Console.WriteLine("| 3. Restore Account                   |");
            Console.WriteLine("| 4. Send Coins                        |");
            Console.WriteLine("| 5. Check Address Balance             |");
            Console.WriteLine("| 6. Transaction History               |");
            Console.WriteLine("| 7. Account Info                      |");
            Console.WriteLine("| 8. Startup Masternode                |");
            Console.WriteLine("| 9. Startup Datanode                  |");
            Console.WriteLine("| 10. Enable API (Turn On and Off)     |");
            Console.WriteLine("| 11. Stop Masternode                  |");
            Console.WriteLine("| 12. Stop Datanode                    |");
            Console.WriteLine("| 13. Exit                             |");
            Console.WriteLine("|======================================|");
        }
    }
}
