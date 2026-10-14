using dnevnik.Entities;
using Microsoft.EntityFrameworkCore;

namespace dnevnik.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<Quarter> Quarters => Set<Quarter>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<Homework> Homeworks => Set<Homework>();
    public DbSet<HomeworkStatus> HomeworkStatuses => Set<HomeworkStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().HasIndex(u => u.Login).IsUnique();
        modelBuilder.Entity<User>().Property(u => u.IsActive).HasDefaultValue(true);
        modelBuilder.Entity<User>().Property(u => u.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<HomeworkStatus>().ToTable("homework_status");

        modelBuilder.Entity<Quarter>()
            .HasIndex(q => new { q.AcademicYear, q.QuarterNumber })
            .IsUnique()
            .HasDatabaseName("uq_quarters_year_number");

        modelBuilder.Entity<Quarter>()
            .Property(q => q.DateStart).HasColumnType("date");
        modelBuilder.Entity<Quarter>()
            .Property(q => q.DateEnd).HasColumnType("date");

        modelBuilder.Entity<Schedule>()
            .HasOne(s => s.Quarter)
            .WithMany(q => q.Schedules)
            .HasForeignKey(s => s.QuarterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Schedule>()
            .HasOne(s => s.Class)
            .WithMany(c => c.Schedules)
            .HasForeignKey(s => s.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Schedule>()
            .HasOne(s => s.Subject)
            .WithMany(su => su.Schedules)
            .HasForeignKey(s => s.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Schedule>()
            .HasOne(s => s.Teacher)
            .WithMany(t => t.Schedules)
            .HasForeignKey(s => s.TeacherId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Schedule>()
            .HasIndex(s => new { s.QuarterId, s.ClassId, s.DayOfWeek, s.LessonNumber })
            .IsUnique()
            .HasDatabaseName("uq_schedule_class_slot");

        modelBuilder.Entity<Schedule>()
            .HasIndex(s => new { s.QuarterId, s.TeacherId, s.DayOfWeek, s.LessonNumber })
            .IsUnique()
            .HasDatabaseName("uq_schedule_teacher_slot");
    }
}