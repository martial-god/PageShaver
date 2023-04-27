﻿using Benny_Scraper.DataAccess.Data;
using Benny_Scraper.DataAccess.Repository.IRepository;

namespace Benny_Scraper.DataAccess.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _db;

        public UnitOfWork(ApplicationDbContext db)
        {
            _db = db;
            Chapter = new ChapterRepository(_db);
            Novel = new NovelRepository(_db);
            NovelList = new NovelListRepository(_db);

        }

        public IChapterRepository Chapter { get; private set; }
        public INovelRepository Novel { get; private set; }
        public INovelListRepository NovelList { get; private set; }

        public Task<int> SaveAsync()
        {
            return _db.SaveChangesAsync();
        }

    }
}
