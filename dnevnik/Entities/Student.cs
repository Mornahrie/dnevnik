using dnevnik.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("students")]
public class Student
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }
    public User? User { get; set; }

    [Column("class_id")]
    public int? ClassId { get; set; }
    public SchoolClass? Class { get; set; }

    [Column("birth_date")]
    public DateTime? BirthDate { get; set; }

    public ICollection<ParentStudent> ParentStudents { get; set; } = new List<ParentStudent>();
    public ICollection<Grade> Grades { get; set; } = new List<Grade>();
    public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
    public ICollection<HomeworkStatus> HomeworkStatuses { get; set; } = new List<HomeworkStatus>();
}