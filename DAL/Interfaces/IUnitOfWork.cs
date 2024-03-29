﻿using NextLevelTrainingApi.DAL.Entities;
using System;

namespace NextLevelTrainingApi.DAL.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// The repository for the user model.
        /// </summary>
        IGenericRepository<Users> UserRepository { get; }
        IGenericRepository<Message> MessageRepository { get; }
        IGenericRepository<Post> PostRepository { get; }
        IGenericRepository<PostCode> PostCodeRepository { get; }
        IGenericRepository<Booking> BookingRepository { get; }
        IGenericRepository<ErrorLog> ErrorLogRepository { get; }
        IGenericRepository<HashTag> HashTagRepository { get; }
        IGenericRepository<Notification> NotificationRepository { get; }
        IGenericRepository<ApiKey> ApiKeyRepository { get; }
        IGenericRepository<CreditHistory> CreditHistoryRepository { get; }
        IGenericRepository<Leads> LeadsRepository { get; }
        IGenericRepository<Responses> ResponsesRepository { get; }
        IGenericRepository<Event> EventRepository { get; }
        IGenericRepository<DynamicNotification> DynamicNotificationRepository { get; }
    }
}
