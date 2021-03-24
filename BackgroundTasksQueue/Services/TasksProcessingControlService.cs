﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;

namespace BackgroundTasksQueue.Services
{
    public interface ITasksProcessingControlService
    {
        public Task<bool> CheckingATaskPackageCompletion(EventKeyNames eventKeysSet, string tasksPackageGuidField);
        public Task<bool> CheckingAllTasksCompletion(EventKeyNames eventKeysSet, string tasksPackageGuidField);
    }

    public class TasksProcessingControlService : ITasksProcessingControlService
    {
        private readonly IBackgroundTasksService _task2Queue;
        private readonly ILogger<TasksProcessingControlService> _logger;
        private readonly ICacheProviderAsync _cache;

        public TasksProcessingControlService(
            ILogger<TasksProcessingControlService> logger,
            ICacheProviderAsync cache,
            IBackgroundTasksService task2Queue)
        {
            _task2Queue = task2Queue;
            _logger = logger;
            _cache = cache;
        }

        private const int IndexBaseValue = 800 * 1000;

        public async Task<bool> CheckingATaskPackageCompletion(EventKeyNames eventKeysSet, string tasksPackageGuidField)
        {
            // проверить значение в ключе сервера - если больше нуля, значит, ещё не закончено
            // если пакет в работе, вернуть true, если пакет закончен - false
            string backServerPrefixGuid = eventKeysSet.BackServerPrefixGuid;
            int totalUnsolvedTasksLeft = await _cache.GetHashedAsync<int>(backServerPrefixGuid, tasksPackageGuidField); // forsake
            return totalUnsolvedTasksLeft > 0;
        }

        public async Task<bool> CheckingAllTasksCompletion(EventKeyNames eventKeysSet, string tasksPackageGuidField) 
        {
            // проверяем текущее состояние пакета задач, если ещё выполняется, возобновляем подписку на ключ пакета
            // если выполнение окончено, подписку возобновляем или нет? но тогда восстанавливаем ключ подписки на вброс пакетов задач
            // возвращаем состояние выполнения - ещё выполняется или уже окончено
            // если выполняется, то true

            // достаём из каждого поля ключа значение (проценты) и вычисляем общий процент выполнения
            double taskPackageState = 0;
            bool allTasksCompleted = true;
            IDictionary<string, TaskDescriptionAndProgress> taskPackage = await _cache.GetHashedAllAsync<TaskDescriptionAndProgress>(tasksPackageGuidField);
            int taskPackageCount = taskPackage.Count;
            _logger.LogInformation(IndexBaseValue + 301, "TasksList fetched - tasks count = {1}.", taskPackageCount);

            foreach (var t in taskPackage)
            {
                var (singleTaskGuid, taskProgressState) = t;
                int taskState = taskProgressState.TaskState.TaskCompletedOnPercent;
                bool isTaskRunning = taskProgressState.TaskState.IsTaskRunning;
                if (isTaskRunning)
                {
                    allTasksCompleted = false; // если хоть одна задача выполняется, пакет не закончен
                }
                if (taskState < 0)
                {
                    // подсчёт всех процентов можно убрать, ориентироваться только на allTasksCompleted
                    // суммарный процент можно считать в другом методе или из этого возвращать принудительно сотню, если true
                    _logger.LogInformation(IndexBaseValue + 311, "One (or more) Tasks do not start yet, taskState = {0}.", taskState);
                    return false;
                }
                // вычислить суммарный процент - всё сложить и разделить на количество
                taskPackageState += taskState;
                _logger.LogInformation(IndexBaseValue + 321, "foreach in taskPackage - Single task No. {1} completed by {2} percents.", singleTaskGuid, taskState);
            }

            double taskPackageStatePercentageDouble = taskPackageState / taskPackageCount;
            int taskPackageStatePercentage = (int)taskPackageStatePercentageDouble;
            _logger.LogInformation(IndexBaseValue + 331, " --- RETURN - this TaskPackage is completed on {0} percents.  \n       ", taskPackageStatePercentage);

            // подписку оформить в отдельном методе, а этот вызывать оттуда
            // можно ставить блокировку на подписку и не отвлекаться на события, пока не закончена очередная проверка

            return allTasksCompleted;
        }
    }
}
