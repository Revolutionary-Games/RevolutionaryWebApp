class Report < ApplicationRecord
  # default_scope server: -> { (acting_user && acting_user.developer?) ? all :
  #                              all.where(public: true) },
  #               client: -> { (Hyperstack::Application.acting_user_id &&
  #                             App.acting_user.developer?) || public }

  scope :visible_to, lambda {|id|
    user = User.find_by_id(id)
    user&.developer? ? all : where(public: true)
  }

  scope :index_by_updated_at,
        server: -> { order('updated_at DESC').select(
                       [:id, :description, :crash_time, :primary_callstack, :solved,
                        :solved_comment, :public, :updated_at, :game_version]) },
        select: -> { sort { |a, b| b.updated_at <=> a.updated_at }}
  
  has_many :duplicates, class_name: "Report", foreign_key: "duplicate_of_id"
  belongs_to :duplicate_of, class_name: "Report", required: false

  validates_uniqueness_of :delete_key
  before_validation :generate_delete_key

  validates :description, presence: true, length: { maximum: 500 }
  validates :notes, length: { maximum: 32000 }, allow_nil: true
  validates :extra_description, length: { maximum: 32000 }, allow_nil: true

  validates :crash_time, presence: true

  validates :reporter_ip, presence: true
  validates :reporter_email, length: { maximum: 255 }, allow_nil: true
  validates :public, inclusion: { in: [ true, false ] }

  validates :processed_dump, presence: true, length: { maximum: 5000000 }

  validates :primary_callstack, presence: true, length: { maximum: 5000 }
  validates :log_files, presence: true, length: { maximum: 5000000 }

  validates :delete_key, presence: true
  validates :solved_comment, length: { maximum: 500 }, allow_nil: true

  validates :game_version, presence: true, length: { minimum: 5, maximum: 80 }

  def generate_delete_key
    begin
      new_key = SecureRandom.base58(32)
    end while Report.find_by_delete_key(new_key)
    self.delete_key = new_key
  end
end
