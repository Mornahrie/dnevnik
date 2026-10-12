using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("classes")]
public class SchoolClass
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("year")]
    public int Year { get; set; }

    public ICollection<Student> Students { get; set; } = new List<Student>();
    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
    public ICollection<Homework> Homeworks { get; set; } = new List<Homework>();
}