# frozen_string_literal: true

# An uploaded devbuild in the system
class DevBuild < ApplicationRecord
  belongs_to :storage_item, dependent: :destroy
  belongs_to :user
  has_and_belongs_to_many :dehydrated_objects, dependent: :destroy, uniq: true

  validates :build_hash, presence: true, length: { maximum: 255, minimum: 15 }
  validates :branch, presence: true, length: { maximum: 255, minimum: 1 }
  validates :platform, presence: true, length: { maximum: 255, minimum: 3 }
  validates :description, presence: false, length: { maximum: 4096, minimum: 1 }

  validates :anonymous, presence: true, inclusion: { in: [true, false] }

  def uploaded?
    storage_item&.highest_version&.uploading === false
  end
end
