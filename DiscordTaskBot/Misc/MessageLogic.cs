using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DiscordTaskBot.Misc
{
    public static class MessageLogic
    {
        /// <summary>
        /// Sends Discord Message with specified task
        /// </summary>
        /// <param name="taskID">ID of the task</param>
        /// <param name="taskData">TaskData object with task's informations</param>
        /// <param name="context">Context of interaction</param>
        /// <returns>Sent message object</returns>
        public static async Task<IUserMessage> SendTaskMessageAsync(string taskID, TaskData taskData, SocketInteractionContext context)
        {
            var (embed, component) = BuildMessage(taskData, taskID);


            await context.Interaction.FollowupAsync(embed: embed, components: component);

            return await context.Interaction.GetOriginalResponseAsync();
        }

        /// <summary>
        /// Updates task's message status
        /// </summary>
        /// <param name="taskData">TaskData with task's informations</param>
        /// <param name="taskID">ID of the task</param>
        /// <param name="message">Message object</param>
        public static async Task UpdateTaskMessageStatus(TaskData taskData, string taskID, IUserMessage message)
        {
            var (embed, component) = BuildMessage(taskData, taskID);

            await message.ModifyAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = component;
            });
        }

        /// <summary>
        /// Moves message to archive channel
        /// </summary>
        /// <param name="taskData">TaskData with task's information</param>
        /// <param name="taskID">ID of the task</param>
        /// <param name="messageComponent">Component of the message, passed as argument in function handling button click</param>
        /// <exception cref="InvalidOperationException">Throws when archive channel is not found</exception>
        public static async Task MoveTaskMessageToArchive(TaskData taskData, string taskID, SocketMessageComponent messageComponent)
        {
            var (embed, component) = BuildMessage(taskData, taskID);

            var oldMessage = await messageComponent.Channel.GetMessageAsync(taskData.MessageID) as IUserMessage;

            var archiveChannelID = ulong.Parse(Environment.GetEnvironmentVariable("ARCHIVE_CHANNEL")!);

            if (oldMessage != null)
                await oldMessage.DeleteAsync();

            var archiveChannel = Bot._client.GetChannel(archiveChannelID) as IMessageChannel;
            if (archiveChannel == null)
                throw new InvalidOperationException("Archive channel not found");

            var newMessage = await archiveChannel.SendMessageAsync(embed: embed, components: component);

            TaskManager.Tasks[taskID].ChannelID = archiveChannelID;
            TaskManager.Tasks[taskID].MessageID = newMessage.Id;
            TaskManager.SaveTasks();
        }

        /// <summary>
        /// Helps build message's body - embed and buttons
        /// </summary>
        /// <param name="taskData">TaskData object with task's informations</param>
        /// <param name="taskID">ID of the task</param>
        /// <returns></returns>
        private static (Embed, MessageComponent?) BuildMessage(TaskData taskData, string taskID)
        {
            string stateName = "";
            string buttonName = "";
            string titleEmoji = "";
            Color embedColor = Color.Default;
            ButtonStyle buttonStyle = ButtonStyle.Secondary;

            var remainingTime = taskData.CompletionDate - DateTime.UtcNow;

            var isLate = false;

            if (remainingTime.TotalHours < 0)
            {
                titleEmoji = "💀 ";
                embedColor = new Color(0, 0, 0);
                isLate = true;
            }
            else if (remainingTime.TotalHours <= 24)
            {
                titleEmoji = "❗ ";
                embedColor = new Color(204, 0, 0);
                isLate = true;
            }

            switch (taskData.State)
            {
                case TaskStates.NOT_STARTED:
                    stateName = "⏳ Not Started";
                    buttonName = "▶️  Start";
                    if (!isLate)
                        embedColor = Color.LightGrey;
                    break;
                case TaskStates.IN_PROGRESS:
                    stateName = "🔨 In Progress";
                    buttonName = "🏁  Complete";
                    buttonStyle = ButtonStyle.Primary;
                    if (!isLate)
                        embedColor = Color.Orange;
                    break;
                case TaskStates.COMPLETE:
                    stateName = "✅ Completed";
                    buttonName = "📥  Archive";
                    embedColor = Color.Green;
                    buttonStyle = ButtonStyle.Success;
                    titleEmoji = "";
                    break;
                case TaskStates.ARCHIVE:
                    stateName = "📦 Archived";
                    embedColor = Color.Purple;
                    titleEmoji = "";
                    break;
            }

            var embed = new EmbedBuilder()
                .WithTitle(titleEmoji + "Task")
                .WithDescription($"{taskData.Description}")
                .AddField("Assigned To", $"<@{taskData.UserID}>", inline: true)  // mention the user
                                                                                 //.AddField("Deadline", taskData.CompletionDate.ToString("MM/dd/yyyy"), inline: true)  // nicer date format
                .AddField("Deadline", GetDiscordTimestamp(taskData.CompletionDate), inline: true)  // discord timestamp
                .AddField("Status", stateName, inline: true)
                .WithColor(embedColor)
                .WithFooter(footer => footer.Text = $"Created on: {taskData.CreationDate:dd/MM/yyyy}")
                .Build();


            if (taskData.State == TaskStates.ARCHIVE)
            {
                return (embed, null);
            }

            var component = new ComponentBuilder()
                .WithButton(buttonName, customId: $"state_{taskID}", buttonStyle)
                .WithButton("Cancel", customId: $"delete_{taskID}", ButtonStyle.Danger)
                .Build();

            return (embed, component);
        }

        /// <summary>
        /// Helps creating discord timestamps
        /// </summary>
        /// <param name="dateTime">DateTime object with date</param>
        /// <param name="format">Format of the timestamp, defaultly R"</param>
        /// <returns>String with ready to use discord timestamp</returns>
        public static string GetDiscordTimestamp(DateTime dateTime, char format = 'R')
        {
            // Upewnij się, że czas jest w UTC
            long unixTime = ((DateTimeOffset)dateTime.ToUniversalTime()).ToUnixTimeSeconds();
            return $"<t:{unixTime}:{format}>";
        }

        /// <summary>
        /// Deletes tasks without existing message on discord, in case user deleted task's message 
        /// </summary>
        public static async Task DailyTaskUpdate()
        {
            List<string> keysToDelete = [];
            foreach (var (taskID, taskData) in TaskManager.Tasks)
            {
                if (TaskManager.GetUserMessageById(taskID) is not IUserMessage message)
                {
                    keysToDelete.Add(taskID);
                    continue;
                }

                await UpdateTaskMessageStatus(taskData, taskID, message);
            }

            foreach (var key in keysToDelete)
            {
                TaskManager.Tasks.Remove(key);
            }
            TaskManager.SaveTasks();
        }
    }
}