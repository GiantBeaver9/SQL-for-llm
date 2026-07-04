using System.Text;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Data;

/// <summary>Applies snake_case to tables and columns so the physical schema reads like the DDL in
/// the design doc (rate_table.profession_class), independent of C# naming.</summary>
public static class SnakeCase
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnake(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnake(property.Name));
            foreach (var key in entity.GetKeys())
                key.SetName(ToSnake(key.GetName()!));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnake(index.GetDatabaseName()!));
        }
    }

    public static string ToSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && name[i - 1] != '_')))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
