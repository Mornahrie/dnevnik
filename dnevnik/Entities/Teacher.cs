using dnevnik.Entities;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("teachers")]
public class Teacher
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(20)]
    [Column("phone")]
    public string? Phone { get; set; }

    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
    public ICollection<Grade> Grades { get; set; } = new List<Grade>();
    public ICollection<Homework> Homeworks { get; set; } = new List<Homework>();
}