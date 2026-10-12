using dnevnik.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("teacher_subject")]
public class TeacherSubject
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("teacher_id")]
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    [Column("subject_id")]
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    [Column("class_id")]
    public int? ClassId { get; set; }
    public SchoolClass? Class { get; set; }
}