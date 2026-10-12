using dnevnik.Entities;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
    
namespace dnevnik.Entities;

[Table("homework_status")]
public class HomeworkStatus
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("homework_id")]
    public int? HomeworkId { get; set; }
    public Homework? Homework { get; set; }

    [Column("student_id")]
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    [Required, MaxLength(20)]
    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("date_submitted")]
    public DateTime? DateSubmitted { get; set; }
}