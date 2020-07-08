# frozen_string_literal: true

# A dehydrated object that is associated with one or more devbuilds
class DehydratedObject < ApplicationRecord
  belongs_to :storage_item, dependent: :destroy
  has_and_belongs_to_many :dev_builds, dependent: :destroy, uniq: true

  validates :sha3, presence: true, length: { maximum: 255, minimum: 15 }

  def uploaded?
    storage_item&.highest_version&.uploading === false
  end
end
