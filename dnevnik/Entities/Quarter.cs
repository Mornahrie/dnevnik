using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("quarters")]
public class Quarter
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("academic_year")]
    public int AcademicYear { get; set; }

    [Column("quarter_number")]
    public byte QuarterNumber { get; set; }

    [Column("date_start")]
    public DateOnly DateStart { get; set; }

    [Column("date_end")]
    public DateOnly DateEnd { get; set; }

    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();

    [NotMapped]
    public string YearLabel => $"{AcademicYear}/{AcademicYear + 1}";

    [NotMapped]
    public string Name => $"{QuarterNumber}-я четверть";

    [NotMapped]
    public string FullName => $"{Name} ({YearLabel})";
}