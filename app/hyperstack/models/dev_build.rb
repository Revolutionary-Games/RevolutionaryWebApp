# frozen_string_literal: true

# An uploaded devbuild in the system
class DevBuild < ApplicationRecord
  belongs_to :storage_item, dependent: :destroy
  belongs_to :user, required: false
  has_and_belongs_to_many :dehydrated_objects, dependent: :destroy, uniq: true

  scope :sort_by_created_at,
        server: -> { order('created_at DESC') },
        select: -> { sort { |a, b| b.created_at <=> a.created_at } }

  scope :sort_by_updated_at,
        server: -> { order('updated_at DESC') },
        select: -> { sort { |a, b| b.updated_at <=> a.updated_at } }

  scope :non_anonymous,
        server: -> { where(anonymous: false) },
        select: -> { !anonymous }

  scope :anonymous,
        server: -> { where(anonymous: true) },
        select: -> { anonymous }

  scope :only_safe,
        server: -> { where('verified = TRUE OR anonymous != TRUE') },
        select: -> { verified || !anonymous }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  validates :build_hash, presence: true, length: { maximum: 255, minimum: 15 }
  validates :branch, presence: true, length: { maximum: 255, minimum: 1 }
  validates :platform, presence: true, length: { maximum: 255, minimum: 3 }
  validates :description, presence: false, length: { maximum: 4096, minimum: 1 },
            allow_nil: true

  validates :anonymous, inclusion: { in: [true, false] }

  def uploaded?
    storage_item&.highest_version&.uploading === false
  end
end
