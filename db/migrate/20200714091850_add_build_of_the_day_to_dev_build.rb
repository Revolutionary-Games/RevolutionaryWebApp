# frozen_string_literal: true

class AddBuildOfTheDayToDevBuild < ActiveRecord::Migration[5.2]
  def change
    add_column :dev_builds, :build_of_the_day, :boolean,
               default: false
  end
end
