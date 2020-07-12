# frozen_string_literal: true

# A linked launcher instance
class LauncherLink < ApplicationRecord
  belongs_to :user

  validates :link_code, presence: true, uniqueness: true,
                        length: { maximum: 255, minimum: 15 }
end
