using System.Data;
using ClothingAPI.Data;
using ClothingAPI.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClothingAPI.Helpers;

public static class TransactionLock
{
    public static async Task AcquireAsync(AppDbContext db, string resource)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required for this lock.");
        if (!db.Database.IsSqlServer()) return; // SQLite serializes test write transactions.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (result < 0) throw new ConflictException("Yêu cầu đang được xử lý. Vui lòng kiểm tra và thử lại.");
    }
}
