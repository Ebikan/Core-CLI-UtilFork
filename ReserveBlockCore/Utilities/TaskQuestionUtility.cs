﻿using ReserveBlockCore.Models;

namespace ReserveBlockCore.Utilities
{
    public class TaskQuestionUtility
    {
        public static async Task<TaskQuestion> CreateTaskQuestion(string type)
        {
            TaskQuestion taskQuestion = new TaskQuestion();

            if(type != null)
            {
                switch (type)
                {
                    case "rndNum":
                        taskQuestion.TaskAnswer = GenerateRandomNumber().ToString();
                        break;
                    case "pickCol":
                        break;
                    default:
                        break;
                }
                
                taskQuestion.TaskType = type;
                taskQuestion.BlockHeight = Program.BlockHeight + 1;
            }

            return taskQuestion;
        }

        public static int GenerateRandomNumber()
        {
            int randomNumber = 0;
            Random rnd = new Random();

            randomNumber = rnd.Next(1, 1000000);

            return randomNumber;
        }

    }
}
