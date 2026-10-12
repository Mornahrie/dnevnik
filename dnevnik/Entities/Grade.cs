using dnevnik.Entities;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("grades")]
public class Grade
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("student_id")]
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    [Column("subject_id")]
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    [Column("teacher_id")]
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    [Column("value")]
    public int Value { get; set; }

    [Column("date")]
    public DateTime Date { get; set; }

    [MaxLength(50)]
    [Column("type")]
    public string? Type { get; set; }

    [Column("comment")]
    public string? Comment { get; set; }
}