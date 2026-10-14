using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("schedule")]
public class Schedule
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("quarter_id")]
    public int QuarterId { get; set; }
    public Quarter Quarter { get; set; } = null!;

    [Column("class_id")]
    public int? ClassId { get; set; }
    public SchoolClass? Class { get; set; }

    [Column("subject_id")]
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    [Column("teacher_id")]
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    [Column("day_of_week")]
    public int DayOfWeek { get; set; }

    [Column("lesson_number")]
    public int LessonNumber { get; set; }

    [MaxLength(20)]
    [Column("cabinet")]
    public string? Cabinet { get; set; }
}