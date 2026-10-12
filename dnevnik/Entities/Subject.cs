using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("subjects")]
public class Subject
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
    public ICollection<Grade> Grades { get; set; } = new List<Grade>();
    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
    public ICollection<Homework> Homeworks { get; set; } = new List<Homework>();
}