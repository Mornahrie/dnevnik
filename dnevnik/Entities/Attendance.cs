using dnevnik.Entities;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("attendance")]
public class Attendance
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

    [Column("date")]
    public DateTime Date { get; set; }

    [Required, MaxLength(20)]
    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("comment")]
    public string? Comment { get; set; }
}