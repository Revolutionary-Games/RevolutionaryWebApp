# frozen_string_literal: true

# User submitted crash report model
class Report < ApplicationRecord
  after_save :send_email_notifications

  # default_scope server: -> { (acting_user && acting_user.developer?) ? all :
  #                              all.where(public: true) },
  #               client: -> { (Hyperstack::Application.acting_user_id &&
  #                             App.acting_user.developer?) || public }

  scope :visible_to, lambda { |id|
    user = User.find_by_id(id)
    user&.developer? ? all : where(public: true)
  }

  scope :index_by_updated_at, -> { order('updated_at DESC') }

  scope :index_by_updated_at_reverse, -> { order('updated_at ASC') }

  scope :index_id_reverse, -> { order('id DESC') }

  scope :not_solved, -> { where(solved: nil).or(where(solved: false)) }

  scope :not_duplicate, -> { where(duplicate_of: nil) }

  belongs_to :duplicate_of, class_name: 'Report', required: false
  has_many :duplicates, class_name: 'Report', foreign_key: 'duplicate_of_id'

  validates_uniqueness_of :delete_key
  before_validation :generate_delete_key

  validates :description, presence: true, length: { maximum: 500 }
  validates :notes, length: { maximum: 32_000 }, allow_nil: true
  validates :extra_description, length: { maximum: 32_000 }, allow_nil: true

  validates :crash_time, presence: true

  validates :reporter_ip, presence: true
  validates :reporter_email, length: { maximum: 255 }, allow_nil: true
  validates :public, inclusion: { in: [true, false] }

  validates :processed_dump, presence: true, length: { maximum: 5_000_000 }

  validates :primary_callstack, presence: true, length: { maximum: 10_000 }
  validates :log_files, presence: true, length: { maximum: 5_000_000 }

  validates :delete_key, presence: true
  validates :solved_comment, length: { maximum: 500 }, allow_nil: true

  validates :game_version, presence: true, length: { minimum: 5, maximum: 80 }

  validate :ensure_duplicate_of_exists

  def ensure_duplicate_of_exists
    return if duplicate_of_id.nil?

    begin
      Report.find(duplicate_of_id)
    rescue ActiveRecord::RecordNotFound
      errors.add(:duplicate_of_id, 'duplicate of must exist')
      false
    end
  end

  def generate_delete_key
    return if delete_key

    new_key = nil
    loop do
      new_key = SecureRandom.base58(32)
      break unless Report.find_by_delete_key(new_key)
    end
    self.delete_key = new_key
  end

  def send_email_notifications
    return unless reporter_email

    # Because this isn't saved yet, the emails are sent with a delay to
    # make the email job get up to date info
    saved_changes.each { |key, _|
      case key
      when 'duplicate_of_id'
        ReportStatusMailer.marked_duplicate(id).deliver_later(wait: 10.seconds)
      when 'solved'
        ReportStatusMailer.solved_changed(id).deliver_later(wait: 10.seconds)
      end
    }
  end
end
